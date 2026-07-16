# Contributing / Developer guide

This is the developer-facing companion to the [README](README.md) (players) and the
[User Guide](USERGUIDE.md) (track authors). It covers how the mod is built, how it hooks the game,
the on-track data schema, and the offline tooling used to keep it working across Liftoff updates.

## Table of contents

- [Architecture](#architecture)
- [Building from source](#building-from-source)
- [How the mod hooks the game](#how-the-mod-hooks-the-game)
- [Adding an author-facing option](#adding-an-author-facing-option)
- [The `MO_*` track schema](#the-mo_-track-schema)
- [`tools/PatchHelper` — the IL-inspection toolkit](#toolspatchhelper--the-il-inspection-toolkit)
- [File & config locations](#file--config-locations)

---

## Architecture

The repo is three .NET projects plus a build-time tool:

```
Liftoff.MovingObjects/             # the BepInEx plugin (runtime mod)          — netstandard2.1
Liftoff.MovingObjects.Patcher/     # BepInEx preloader patcher (Mono.Cecil)    — net35
tools/PatchHelper/                 # build-time tool: applies the patcher's    — net10.0
                                   #   logic offline + IL-inspection verbs
lib/                               # populated by build.ps1, gitignored
build.ps1                          # the build entry point
```

- **Plugin** (`Liftoff.MovingObjects`, `netstandard2.1`) — the runtime mod BepInEx loads. It reads
  the `mo_*` metadata on track items and attaches the players/behaviours that drive motion at flight
  time, and it adds the editor windows. Entry point: `Plugin.cs`.
- **Patcher** (`Liftoff.MovingObjects.Patcher`, `net35`, Mono.Cecil 0.10) — a BepInEx *preloader
  patcher*. At game-load time it Cecil-injects the serializable `MO_*` types and the `mo_*` fields
  into `Assembly-CSharp.dll` so the mod's data round-trips through the game's own `TrackBlueprint`
  XML. All the injection logic is in `Patcher.cs`.
- **PatchHelper** (`tools/PatchHelper`, `net10.0`, Mono.Cecil 0.11) — a build-time console tool. It
  **links `Patcher.cs` in as shared source**, so it runs the *identical* injection logic offline to
  produce a patched reference `Assembly-CSharp.dll` in `lib/` that the plugin can compile against. It
  also carries a set of [IL-inspection verbs](#toolspatchhelper--the-il-inspection-toolkit) for
  reverse-engineering the obfuscated game assembly.

## Building from source

Only needed if you're modifying the mod or rebuilding against a newer game version.

**Requirements**
- **.NET SDK 10** — required even though the shipped assemblies target `netstandard2.1` / `net35`,
  because `tools/PatchHelper` targets `net10.0` and `build.ps1` runs it via `dotnet run`. There is no
  `global.json` pinning the SDK.
- **Liftoff installed via Steam.**

**Run** the helper script from the repository root:

```powershell
./build.ps1                                       # build only
./build.ps1 -Deploy                               # build and copy to BepInEx
./build.ps1 -LiftoffPath 'D:\Steam\...\Liftoff'   # custom install path (default is the 32-bit Steam path)
```

The script:

1. Copies the engine DLLs Liftoff ships with (from `<Liftoff>/Liftoff_Data/Managed`) into `./lib/`.
2. Builds `Liftoff.MovingObjects.Patcher`.
3. Runs `tools/PatchHelper` to write a patched copy of `Assembly-CSharp.dll` into `./lib/`. **The
   plugin cannot compile against an unpatched assembly** because it references `MO_AnimationOptions`,
   `MO_Animation`, and `MO_TriggerOptions` — types the patcher creates rather than fields the game
   ships with. This step makes those types available at compile time too.
4. Builds `Liftoff.MovingObjects` (the plugin).
5. With `-Deploy`, copies both DLLs into `<LiftoffPath>/BepInEx/{plugins,patchers}`.

## How the mod hooks the game

`Assembly-CSharp.dll` is obfuscated, so most hook targets are held positionally (public methods
resolved by name, obfuscated parameter types accessed by position). The main Harmony patch points:

- `FlightManager.Start` + the parameterless `onDroneResetStart` / `onDroneResetDone` events — where
  animation/physics injection happens and re-runs on each drone reset.
- `PopupShareContent.ShareItem` — patched **by name only** (single overload) to override the workshop
  preview with a local `preview.png` without naming the obfuscated third parameter.
- `TrackItemShowTextTrigger.OnDroneEnter` — the Show-Text duration override.
- `TrackEditor.AssignIDToTrackItem` (add) and `TrackEditor.RemoveTrackItem` (remove) — the two shared
  chokepoints the undo/redo system captures through, so native and mod edits are both covered.
- `TrackEditorGUI.Start`, `TrackEditorEditWindow.AtLeastOneItemAvailable`, and the item-spawn chain
  (`GetTrackItemPrefab` → `CreateNewTrackItem` → `AssignIDToTrackItem` → `ApplyBlueprint`).

**When a game update breaks the mod, check the hook points first.** Run
[`tools/PatchHelper audit`](#toolspatchhelper--the-il-inspection-toolkit) — it verifies each of these
targets still exists and reports its resolved parameter types. For the historical record of the three
issues that broke upstream 1.0.14 (and how v1.1.0 fixed them), see
[the changelog](CHANGELOG.md#why-upstream-1014-stopped-working).

## Adding an author-facing option

A new author-facing option generally touches three layers:

1. **A serialized field** in `Liftoff.MovingObjects.Patcher/Patcher.cs` — added to the injected
   `MO_*` type (or to `TrackBlueprint`) so it round-trips in the track XML. See
   [the schema below](#the-mo_-track-schema).
2. **The editor UI.** The `liftoffui.bundle` can't be rebuilt without Unity, so new controls are
   **added in code** in `AnimationEditorWindow.cs` (follow the `Ensure*Controls` methods) or
   `PlacementUtilsWindow.cs`.
3. **The runtime reader** — `Player/AnimationPlayer.cs`, `Player/PhysicsPlayer.cs`, or
   `TriggerBehavior.cs`.

Features that reuse existing data or are pure runtime changes are the cheapest. Keep every new option
**off / zero by default** so existing tracks are unaffected. If a release changes how a track *plays*
(not just editor behaviour), bump `Plugin.MinCompatibleVersion` so older mods refuse the track cleanly
(see [file & config locations](#file--config-locations)).

## The `MO_*` track schema

The patcher injects **three serializable types** and **six fields onto the existing `TrackBlueprint`**.
All fields are `public` with an `[XmlElement("<name>")]` attribute; vector fields reuse the game's
`SerializableVector3`. This is the authoritative schema (`Patcher.cs`).

### `MO_AnimationOptions` (28 fields)

| Field | Type | | Field | Type |
|-------|------|-|-------|------|
| `teleportToStart` | bool | | `orbitEnabled` | bool |
| `simulatePhysics` | bool | | `orbitRadius` | float |
| `simulatePhysicsTime` | float | | `orbitSpeed` | float |
| `simulatePhysicsDelay` | float | | `orbitAxis` | SerializableVector3 |
| `simulatePhysicsWarmupDelay` | float | | `orbitFacePath` | bool |
| `animationWarmupDelay` | float | | `phaseOffset` | float |
| `animationRepeats` | int | | `randomizePhase` | bool |
| `triggerAction` | int | | `launchImpulse` | SerializableVector3 |
| `easingMode` | int | | `launchTorque` | SerializableVector3 |
| `pingPong` | bool | | `overrideGravity` | bool |
| `spinnerEnabled` | bool | | `gravityScale` | float |
| `spinAxis` | SerializableVector3 | | `linearDrag` | float |
| `spinSpeed` | float | | `angularDrag` | float |
| | | | `mass` | float |
| | | | `killOnContact` | bool |

### `MO_TriggerOptions` (20 fields)

| Field | Type | | Field | Type |
|-------|------|-|-------|------|
| `triggerName` | string | | `boostEnabled` | bool |
| `triggerTarget` | string | | `speedMultiplier` | float |
| `triggerTeleport` | bool | | `targetSpeed` | float |
| `triggerMinSpeed` | float | | `windEnabled` | bool |
| `triggerMaxSpeed` | float | | `forceVector` | SerializableVector3 |
| `seamlessTeleport` | bool | | `forceMode` | int (0 = Force, 1 = Acceleration) |
| `exitSpeed` | float | | `forceLocalSpace` | bool |
| `triggerOnce` | bool | | `routeBySpeed` | bool |
| `triggerCooldown` | float | | `routeSpeedThreshold` | float |
| `sequentialTargets` | bool | | `playSoundOnTrigger` | bool |

### `MO_Animation` (one keyframe step; 4 fields)

| Field | Type |
|-------|------|
| `delay` | float |
| `time` | float |
| `position` | SerializableVector3 |
| `rotation` | SerializableVector3 |

### Fields injected onto `TrackBlueprint` (6)

| Field | Type | Purpose |
|-------|------|---------|
| `mo_animationSteps` | `List<MO_Animation>` | keyframe list |
| `mo_animationOptions` | `MO_AnimationOptions` | animation + physics config |
| `mo_triggerOptions` | `MO_TriggerOptions` | trigger/teleport config |
| `mo_groupId` | string | group membership GUID |
| `mo_minModVersion` | string | min mod version to play correctly (v1.3.7+) |
| `mo_textDisplayTime` | float | Show-Text overlay duration, seconds (0 = game default) |

## `tools/PatchHelper` — the IL-inspection toolkit

`PatchHelper` dispatches on its first argument. The default (verbless) mode is what `build.ps1` runs;
the other verbs are an offline Cecil-based inspection toolkit for the obfuscated game assembly — handy
when a game update moves things.

| Invocation | What it does |
|------------|--------------|
| `PatchHelper <in.dll> <out.dll>` | **Patch mode** (default). Applies `Patcher.Patch` and writes the patched reference assembly. This is the build step. |
| `PatchHelper explore <asm.dll> <keyword>...` | Dump every type whose full name contains any keyword — all fields (type + name), events, and methods (return type, name, parameters). A type/member browser. |
| `PatchHelper audit <asm.dll>` | Verify the mod's hook points still exist. Checks a hardcoded list of `(Type, Method)` targets (e.g. `TrackEditorGUI.Start`, `PopupShareContent.ShareItem`, the drag handlers, `FlightManager.ResetDroneRoutine`, the `LevelInitSequence` methods) and prints resolved parameter types or `TYPE MISSING` / `METHOD MISSING`. **Run this first after a Liftoff update.** |
| `PatchHelper search <asm.dll> <needle>` | Scan every method body for `Ldstr` string literals containing the needle (case-insensitive) and report `Type.Method` + the string. Finds where UI text / constants live. |
| `PatchHelper il <asm.dll> <TypeName> [MethodSubstr]` | Dump the full IL of matching methods (recurses nested types). |
| `PatchHelper il <asm.dll> --scan <instrSubstr> [--full]` | Dump every method whose IL contains an instruction substring. |

## File & config locations

- **BepInEx config**: `BepInEx/config/Liftoff.MovingObjects.cfg` (created on first run). One key:
  `[Experimental] SpectatorAnimationSync` (default `false`).
- **Stamps**: `<Liftoff>/mo_stamps/<name>.xml`, written by `Utils/StampIO.cs`. The file is a
  `<MO_Stamp>` root containing one `<item>` per `TrackBlueprint` (an `XmlSerializer` over a `StampSet`
  wrapper), **not** a bare blueprint.
- **Workshop preview override**: `<Liftoff>/preview.png` (Liftoff root), read by the `ShareItem` patch.
- **Version gate**: `Plugin.MinCompatibleVersion` is the value stamped into freshly-saved tracks
  (`mo_minModVersion`). It is deliberately separate from — and lags — `PLUGIN_VERSION`; bump it only
  when a release changes how a track plays. At flight time the plugin refuses to animate any track
  whose stamped floor exceeds the running version.
- **Log sources**: BepInEx log sources are named `Liftoff.MovingObjects.<ClassName>`; the load banner
  is `Liftoff.MovingObjects <version> loaded`.

---

*Original mod by [ps-hek](https://github.com/ps-hek/Liftoff.MovingObjects); this fork keeps it working
against current Liftoff builds and adds the moving-objects feature set.*
