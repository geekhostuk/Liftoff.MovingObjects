<#
.SYNOPSIS
  Build Liftoff.MovingObjects from source against the installed game.

.DESCRIPTION
  Automates the full build pipeline:
    1. Copies referenced DLLs from the Liftoff install into ./lib/.
    2. Builds the BepInEx patcher (Liftoff.MovingObjects.Patcher).
    3. Runs PatchHelper to inject the mod's serializable fields into a
       reference copy of Assembly-CSharp.dll, written to ./lib/. Without
       this step the plugin cannot compile, because it references types
       (MO_AnimationOptions, MO_Animation, MO_TriggerOptions) that the
       patcher creates only at game-load time.
    4. Builds the plugin (Liftoff.MovingObjects).
    5. Optionally deploys the resulting DLLs into the BepInEx folder of
       the Liftoff install.

.PARAMETER LiftoffPath
  Liftoff install root. Defaults to the standard 32-bit Steam path.

.PARAMETER Configuration
  Build configuration. Defaults to Release.

.PARAMETER Deploy
  When set, copies the built plugin and patcher into the game's BepInEx
  plugins/ and patchers/ folders.

.EXAMPLE
  ./build.ps1
  Builds without deploying.

.EXAMPLE
  ./build.ps1 -Deploy
  Builds and installs into the default Liftoff install.

.EXAMPLE
  ./build.ps1 -LiftoffPath 'D:\SteamLibrary\steamapps\common\Liftoff' -Deploy
#>
param(
    [string]$LiftoffPath = 'C:\Program Files (x86)\Steam\steamapps\common\Liftoff',
    [string]$Configuration = 'Release',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Test-Path $LiftoffPath)) {
    throw "Liftoff install not found at '$LiftoffPath'. Pass -LiftoffPath to override."
}

$managed = Join-Path $LiftoffPath 'Liftoff_Data\Managed'
if (-not (Test-Path $managed)) {
    throw "Managed assemblies not found at '$managed'."
}

$libDir = Join-Path $repoRoot 'lib'
New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$gameDlls = @(
    'UnityEngine.dll',
    'UnityEngine.AssetBundleModule.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.InputLegacyModule.dll',
    'UnityEngine.PhysicsModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.UIElementsModule.dll',
    'UnityEngine.ImageConversionModule.dll',
    'UnityEngine.AudioModule.dll',

    # Photon (PUN). The game's netcode is Photon and these ship unobfuscated, so we can bind to
    # them directly — that's how the spectator sync reads the room-synchronised clock and
    # identifies the player behind a [PunRPC] call.
    'PhotonUnityNetworking.dll',
    'PhotonRealtime.dll',
    'Photon3Unity3D.dll'
)

Write-Host '==> Copying engine DLLs to ./lib' -ForegroundColor Cyan
foreach ($dll in $gameDlls) {
    $src = Join-Path $managed $dll
    if (-not (Test-Path $src)) { throw "Missing engine DLL: $src" }
    Copy-Item $src (Join-Path $libDir $dll) -Force
}

Write-Host '==> Building patcher' -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot 'Liftoff.MovingObjects.Patcher\Liftoff.MovingObjects.Patcher.csproj') -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed.' }

Write-Host '==> Patching reference Assembly-CSharp.dll into ./lib' -ForegroundColor Cyan
$asmIn = Join-Path $managed 'Assembly-CSharp.dll'
$asmOut = Join-Path $libDir 'Assembly-CSharp.dll'
dotnet run --project (Join-Path $repoRoot 'tools\PatchHelper\PatchHelper.csproj') -c $Configuration -- $asmIn $asmOut | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'PatchHelper run failed.' }

Write-Host '==> Building plugin' -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot 'Liftoff.MovingObjects\Liftoff.MovingObjects.csproj') -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$pluginOut = Join-Path $repoRoot "bin\$Configuration\BepInEx\plugins\Liftoff.MovingObjects.dll"
$patcherOut = Join-Path $repoRoot "bin\$Configuration\BepInEx\patchers\Liftoff.MovingObjects.Patcher.dll"

Write-Host ''
Write-Host "Plugin:  $pluginOut" -ForegroundColor Green
Write-Host "Patcher: $patcherOut" -ForegroundColor Green

if ($Deploy) {
    $bepinex = Join-Path $LiftoffPath 'BepInEx'
    if (-not (Test-Path $bepinex)) {
        throw "BepInEx not installed at '$bepinex'. Install BepInEx 5 first."
    }
    $pluginDst = Join-Path $bepinex 'plugins\Liftoff.MovingObjects.dll'
    $patcherDst = Join-Path $bepinex 'patchers\Liftoff.MovingObjects.Patcher.dll'
    New-Item -ItemType Directory -Force -Path (Split-Path $pluginDst) | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $patcherDst) | Out-Null
    Copy-Item $pluginOut $pluginDst -Force
    Copy-Item $patcherOut $patcherDst -Force
    Write-Host ''
    Write-Host "Deployed to: $bepinex" -ForegroundColor Green
}
