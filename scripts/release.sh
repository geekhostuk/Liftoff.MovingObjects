#!/usr/bin/env bash
#
# Build both Moving Objects packages and, with --publish, release them.
#
#   Liftoff.MovingObjects-<version>.zip       the full mod, with the track editor
#   Liftoff.MovingObjects-Race-<version>.zip  race only: flies Moving Objects tracks, no editor
#
# Both are laid out BepInEx-relative and carry the same patcher, which is required to fly: it adds
# the mo_* fields to Assembly-CSharp that tracks are read into. The two plugins share a GUID and a
# file name, so a pilot installs one or the other, never both.
#
# The plugin compiles against Liftoff's own assemblies, which can't be redistributed, so releases
# are built here on a machine with the game installed.
#
# Usage:
#   scripts/release.sh              # build + package the current <Version>
#   scripts/release.sh --publish    # ... then publish: both zips on GitHub, the race zip on the JMT site
#
# --publish needs a tag v<version> pushed and checked out, `gh` signed in, and a JMT release key in
# JMT_RELEASE_TOKEN or as a `jmt-release-token:<key>` line in the .env of the folder above this
# repo. The key is never printed. JMT_SITE_URL overrides the site. LIFTOFF_DIR overrides the game.

set -euo pipefail

cd "$(dirname "$0")/.."

publish=0
[ "${1:-}" = "--publish" ] && publish=1

name=Liftoff.MovingObjects
version=$(grep -oP '(?<=<Version>)[^<]+' "$name/$name.csproj")
tag="v$version"
outdir="dist/$tag"
full_zip="$name-$version.zip"
race_zip="$name-Race-$version.zip"
# The JMT site keeps one fixed name per product; the panel installs by it.
site_zip="$name-Race.zip"

echo "==> version $version  tag $tag"

if [ "$publish" -eq 1 ]; then
  if [ -n "$(git status --porcelain)" ]; then
    echo "!! working tree is dirty — the build would not match any commit" >&2
    git status --short >&2
    exit 1
  fi
  if ! git rev-parse "$tag" >/dev/null 2>&1; then
    echo "!! tag $tag does not exist — create and push it first" >&2
    exit 1
  fi
  if [ "$(git rev-parse HEAD)" != "$(git rev-parse "$tag^{commit}")" ]; then
    echo "!! HEAD is not $tag — check out the tag before building a release" >&2
    exit 1
  fi
fi

# ---------------------------------------------------------------- build (as build.ps1 does)

if [ -z "${LIFTOFF_DIR:-}" ]; then
  for dir in "$HOME/.local/share/Steam/steamapps/common/Liftoff" "$HOME/.steam/steam/steamapps/common/Liftoff"; do
    [ -d "$dir/Liftoff_Data/Managed" ] && { LIFTOFF_DIR=$dir; break; }
  done
fi
managed="${LIFTOFF_DIR:-}/Liftoff_Data/Managed"
[ -d "$managed" ] || { echo "!! Liftoff not found; set LIFTOFF_DIR" >&2; exit 1; }

echo "==> copying engine DLLs from $managed"
mkdir -p lib
for dll in UnityEngine UnityEngine.AssetBundleModule UnityEngine.CoreModule UnityEngine.IMGUIModule \
           UnityEngine.InputLegacyModule UnityEngine.PhysicsModule UnityEngine.UI \
           UnityEngine.TextRenderingModule UnityEngine.UIElementsModule \
           UnityEngine.ImageConversionModule UnityEngine.AudioModule \
           PhotonUnityNetworking PhotonRealtime Photon3Unity3D; do
  cp "$managed/$dll.dll" lib/
done

rm -rf bin

echo "==> building the patcher"
dotnet build "$name.Patcher/$name.Patcher.csproj" -c Release -v quiet -nologo

echo "==> patching the reference Assembly-CSharp.dll"
dotnet run --project tools/PatchHelper/PatchHelper.csproj -c Release -- \
  "$managed/Assembly-CSharp.dll" lib/Assembly-CSharp.dll

for config in Release Race; do
  echo "==> building the plugin ($config)"
  dotnet build "$name/$name.csproj" -c "$config" -v quiet -nologo
done

# ---------------------------------------------------------------- package

echo "==> packaging"
rm -rf "$outdir" && mkdir -p "$outdir"
package() {
  local config=$1 zip=$2 stage
  stage=$(mktemp -d)
  mkdir -p "$stage/BepInEx/plugins" "$stage/BepInEx/patchers"
  cp "bin/$config/BepInEx/plugins/$name.dll" "bin/$config/BepInEx/plugins/$name.deps.json" "$stage/BepInEx/plugins/"
  cp "bin/Release/BepInEx/patchers/$name.Patcher.dll" "$stage/BepInEx/patchers/"
  ( cd "$stage" && zip -qr "$OLDPWD/$outdir/$zip" BepInEx )
  rm -rf "$stage"
}
package Release "$full_zip"
package Race "$race_zip"
cp "$outdir/$race_zip" "$outdir/$site_zip"
( cd "$outdir" && sha256sum "$full_zip" "$race_zip" "$site_zip" > SHA256SUMS.txt )

echo
ls -l "$outdir"
cat "$outdir/SHA256SUMS.txt"
echo

if [ "$publish" -eq 0 ]; then
  echo "==> release files in $outdir; publish them with scripts/release.sh --publish"
  exit 0
fi

# The release notes are this version's CHANGELOG section.
notes=$(awk -v v="$version" '
  $0 ~ "^## \\[" v "\\]" { on = 1; next }
  on && /^## \[/ { exit }
  on { print }
' CHANGELOG.md | sed '/./,$!d')

# ---------------------------------------------------------------- GitHub

echo "==> publishing $tag on GitHub"
if gh release view "$tag" >/dev/null 2>&1; then
  gh release upload "$tag" "$outdir/$full_zip" "$outdir/$race_zip" --clobber
  echo "    uploaded both zips to the existing release"
else
  gh release create "$tag" "$outdir/$full_zip" "$outdir/$race_zip" --title "$tag" --notes "$notes"
  echo "    created the release"
fi

# ---------------------------------------------------------------- JMT site

site="${JMT_SITE_URL:-https://jmtfpv.com}"
site="${site%/}"
token="${JMT_RELEASE_TOKEN:-}"
if [ -z "$token" ] && [ -f ../.env ]; then
  token=$(sed -n 's/^[[:space:]]*jmt-release-token[[:space:]]*[:=][[:space:]]*//p' ../.env | head -1 | tr -d '[:space:]')
fi
[ -n "$token" ] || { echo "!! no release key: make one at $site/admin, then set JMT_RELEASE_TOKEN or add jmt-release-token:<key> to ../.env" >&2; exit 1; }

# One request; the answer body on stdout, and a refusal is fatal with the site's reason.
call() {
  local method=$1 path=$2; shift 2
  local out code
  out=$(mktemp)
  code=$(curl -sS -o "$out" -w '%{http_code}' -X "$method" -H "authorization: Bearer $token" "$@" "$site$path")
  if [ "$code" -lt 200 ] || [ "$code" -ge 300 ]; then
    echo "!! $method $path: $code $(head -c 300 "$out")" >&2
    rm -f "$out"
    exit 1
  fi
  cat "$out"
  rm -f "$out"
}

echo "==> publishing movingobjects $version to $site"
releases=$(call GET /api/admin/releases)
existing=$(printf '%s' "$releases" | python3 -c '
import json, sys
for r in json.load(sys.stdin):
    if r["product"] == sys.argv[1] and r["version"] == sys.argv[2]:
        print(r["id"], r["status"])
' movingobjects "$version")
id=${existing%% *}
status=${existing#* }

if [ "$status" = "published" ]; then
  echo "    $version is already published — nothing to do"
  exit 0
fi
if [ -z "$id" ]; then
  body=$(python3 -c 'import json, sys; print(json.dumps({"product": sys.argv[1], "version": sys.argv[2], "notes": sys.argv[3]}))' movingobjects "$version" "$notes")
  created=$(call POST /api/admin/releases -H 'content-type: application/json' --data-binary "$body")
  id=$(printf '%s' "$created" | python3 -c 'import json, sys; print(json.load(sys.stdin)["id"])')
  echo "    created the draft"
else
  echo "    carrying on with the $status release already there"
fi

sum=$(awk -v f="$site_zip" '$2 == f { print $1 }' "$outdir/SHA256SUMS.txt")
call PUT "/api/admin/releases/$id/files/$site_zip?sha256=$sum" \
  -H 'content-type: application/octet-stream' --data-binary "@$outdir/$site_zip" >/dev/null
echo "    uploaded $site_zip (checked)"

call POST "/api/admin/releases/$id/publish" >/dev/null
echo "==> published movingobjects $version; Liftoff Control installs the race build from the JMT site"
