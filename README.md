# Liftoff MovingObjects Mod

> **Warning!** This project is not official and is not supported by the Liftoff game developers.

> **Warning!** Maps built with this mod must be flown with the mod installed. Without the mod, animations and physics on those maps will not run.

A Liftoff mod that adds animated and physics-driven track objects, plus the editor extensions used to author them.

## What this is for

Most people who install this mod do so to **fly community-made maps that use moving objects** rather than to author new ones. The animation, physics, and trigger code in the plugin reads metadata that mappers embed in `TrackBlueprint` items, then attaches runtime components that drive the motion at flight time. With the mod installed, those maps animate; without it, the same maps load but everything stays still.

If you've been pointed at this mod by a community or league, you most likely just want the runtime — see [Install](#install) below.

### Communities using this mod

- **[JMT FPV](https://jmtfpv.com)** runs a multiplayer room called **JMT-MOD** that races on tracks built around moving objects from this mod. JMT FPV publishes their own setup walkthrough at <https://jmtfpv.com/install> — follow that for the JMT-specific server/lobby steps; the *mod-side* installation below is the same regardless of which community you're flying with.

## About this fork

This is a [geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects) of [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects). **All credit for the mod itself goes to [ps-hek](https://github.com/ps-hek)** — they designed it, built it, authored the patcher and editor windows, and shipped the maps community has been racing on. This fork exists only to keep the mod working: the upstream release was last published in early 2024 and stopped working against current Liftoff builds (Unity 2022.3, BepInEx 5.4.23). The prebuilt 1.0.14 plugin would load but objects on modded maps would no longer animate. v1.1.0 in this fork restores the runtime against the current game.

If you're looking for the original project, the commit history of new-feature work, or want to file an issue against the design rather than the modernization, please go to [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects).

### What's verified working in v1.1.0

- Object animation, physics, and triggers on community maps (e.g. *Honkey Kong*).
- Flying through the JMT-MOD multiplayer room.
- Track editor extensions (animation editor window, placement utilities) for authors.
- Animations correctly re-bind after switching tracks within a session.

### Known issues

- **Workshop preview overwrite (`PopupShareContent.ShareItem`) is currently disabled.** The game's `ShareItem` method gained a third parameter that uses an obfuscated type, so the original 2-arg Harmony patch could not bind. HarmonyX throws on a missing target, which previously aborted *all* of the mod's patches. The patch is removed until the new third parameter type is referenceable; nothing else depends on it.
- **Editor "Object count" / "Triangle count" always read `0`.** The placement-utils window (F2) used to refresh these stats once per second via `InvokeRepeating`, which re-scanned every object on the map and summed the triangle count across all child meshes each second — a visible stutter every second on large maps. That polling is now disabled and the labels are pinned to `0`. This only affects the two informational counters; placement, animation, physics, and triggers are unchanged.

### Why upstream 1.0.14 stopped working

Three independent issues, uncovered via runtime tracing:

1. **`PopupShareContent.ShareItem` signature changed.** HarmonyX raised an exception out of `Harmony.CreateAndPatchAll`, aborting registration of every other patch in the plugin. The mod loaded, but no patches were actually attached.
2. **The plugin's `OnDestroy` called `Harmony.UnpatchSelf()`.** The current game destroys the BepInEx plugin GameObject during the bootstrap-scene unload, very early in startup. Once the broken `ShareItem` patch was removed and the rest *did* attach, this teardown promptly removed them again before any flight session began.
3. **Game refactor: `LevelInitSequence.InitializeLevel` and `FlightManager.ResetDroneRoutine` are now dead code.** The methods still exist in `Assembly-CSharp.dll` (so Harmony resolves them silently) but they are never called by the current game flow. Animation/physics injection now hooks `FlightManager.Start` and subscribes to the parameterless `onDroneResetStart` / `onDroneResetDone` events instead.

## Features

### Object animation
Adds step-by-step animation for objects (authored via the in-game animation editor window).

![Animation demo](images/animation.gif)

### Physics

Adds physics to objects.

![Physics demo](images/physics.gif)

### Unlock blueprint objects

Allow placing objects from the Blueprint map on any map.

![Blueprint objects demo](images/blueprint.png)

## Install

If you only want to play modded maps, this is all you need.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your Liftoff folder. (Specifically, the 64-bit Mono build of BepInEx 5.4.x.)
2. Download `Liftoff.MovingObjects-1.1.0.zip` from the [latest release](https://github.com/geekhostuk/Liftoff.MovingObjects/releases/latest).
3. Extract the zip into your Liftoff install folder (the one that contains `Liftoff.exe`). It writes:
   - `BepInEx/plugins/Liftoff.MovingObjects.dll`
   - `BepInEx/patchers/Liftoff.MovingObjects.Patcher.dll`
4. Launch Liftoff. Modded maps should now animate.

If you're flying with JMT FPV, follow <https://jmtfpv.com/install> for the lobby/server side after the mod is installed.

## Building from source

This is only needed if you're modifying the mod or want to rebuild against a newer game version.

Requirements:
- .NET SDK 10.
- Liftoff installed via Steam.

Run the helper script from the repository root:

```powershell
./build.ps1                                       # build only
./build.ps1 -Deploy                               # build and copy to BepInEx
./build.ps1 -LiftoffPath 'D:\Steam\...\Liftoff'   # custom install path
```

The script:

1. Copies the engine DLLs Liftoff ships with into `./lib/`.
2. Builds `Liftoff.MovingObjects.Patcher` (BepInEx preloader patcher; injects the `MO_*` serializable types into `Assembly-CSharp.dll` at game-load time).
3. Runs `tools/PatchHelper` to write a patched copy of `Assembly-CSharp.dll` into `./lib/`. **The plugin cannot compile against an unpatched assembly** because it references `MO_AnimationOptions`, `MO_Animation`, and `MO_TriggerOptions` — types the patcher creates rather than fields the game ships with. This step makes those types available at compile time too.
4. Builds `Liftoff.MovingObjects` (the BepInEx plugin).
5. Optionally copies both DLLs into `<LiftoffPath>/BepInEx/{plugins,patchers}`.

### Layout

```
Liftoff.MovingObjects/             # the BepInEx plugin (runtime mod)
Liftoff.MovingObjects.Patcher/     # the BepInEx preloader patcher (Cecil-injects MO_* types)
tools/PatchHelper/                 # build-time tool: applies the patcher's logic offline
                                   # to produce the lib/Assembly-CSharp.dll reference DLL
lib/                               # populated by build.ps1, gitignored
build.ps1                          # the build entry point
```

## Maps (sampler)

* [MOD_PSHEK_FRPV_BANDO-NICE_1](https://steamcommunity.com/sharedfiles/filedetails/?id=3174317892)
* [Honkey Kong (3 Laps)](https://steamcommunity.com/sharedfiles/filedetails/?id=3175684498)

## Credits

- **[ps-hek](https://github.com/ps-hek)** — original author and maintainer of the mod, the patcher, the editor UI, and the published maps. Everything user-facing about this mod is their work.
- **[geekhostuk](https://github.com/geekhostuk)** — maintains this fork, modernizing the mod to keep working with newer Liftoff builds.

If you find this mod useful, please go give the upstream repo a star.
