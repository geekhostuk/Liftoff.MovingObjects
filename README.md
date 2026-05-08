# Liftoff MovingObjects Mod

> **Warning!** This project is not official and is not supported by Liftoff game developers!

> **Warning!** Created maps must be run with the mod installed. If you run a map with animation in the game without the mod animations will not work.

Mod for Liftoff game to extend functionality of the track editor.

## About this fork

This is a [geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects) of [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects). The upstream release was last published in early 2024 and stopped working against current Liftoff builds (Unity 2022.3, BepInEx 5.4.23) — the prebuilt plugin loaded but objects on modded maps would not animate. This fork modernizes the mod so it works against the current game and adds a build script that produces working binaries from source.

### What's verified working

- Object animations on community maps (e.g. *Honkey Kong*).
- Track editor UI extensions (animation editor window, placement utilities).

### Known issues

- **Workshop preview overwrite (`PopupShareContent.ShareItem`) is currently disabled.** The game's `ShareItem` method gained a third parameter that uses an obfuscated type, so the original 2-arg Harmony patch could not bind. HarmonyX throws on a missing target, which previously aborted *all* of the mod's patches. The patch is removed until the new third parameter type is referenceable; nothing else depends on it.

### Why the prebuilt 1.0.14 stopped working

Three independent issues, all uncovered by adding diagnostic logging:

1. **`PopupShareContent.ShareItem` signature changed.** HarmonyX raised an exception out of `Harmony.CreateAndPatchAll`, aborting registration of every other patch in the plugin. The mod loaded, but no patches were actually attached.
2. **The plugin's `OnDestroy` called `Harmony.UnpatchSelf()`.** The current game destroys the BepInEx plugin GameObject during the bootstrap-scene unload, very early in startup. Once the broken `ShareItem` patch was removed and the rest of the patches *did* attach, this teardown promptly removed them again before any flight session began.
3. **Game refactor: `LevelInitSequence.InitializeLevel` and `FlightManager.ResetDroneRoutine` are now dead code.** The methods still exist in `Assembly-CSharp.dll` (so Harmony resolves them silently) but they are never called by the current game flow. Animation/physics injection now hooks `FlightManager.Start` and subscribes to the parameterless `onDroneResetStart` / `onDroneResetDone` events instead.

## Features

### Object animation
Adds step-by-step animation for objects

![Animation demo](images/animation.gif)

### Physics

Adds physics to objects

![Physics demo](images/physics.gif)

### Unlock blueprint objects

Allow to place objects from the Blueprint map on any map

![Blueprint objects demo](images/blueprint.png)

## Installation

 1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your Liftoff folder.
 2. Open the game directory (Steam → Liftoff → Manage → Browse local files).
 3. Build from source (see below) or download a release once available, and copy the resulting files so that:
    - `BepInEx/plugins/Liftoff.MovingObjects.dll` exists
    - `BepInEx/patchers/Liftoff.MovingObjects.Patcher.dll` exists

## Building from source

Requirements:
- .NET SDK 10 (older SDKs work too if you can target net35 / net10.0 reference assemblies).
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

## Maps

* [MOD_PSHEK_FRPV_BANDO-NICE_1](https://steamcommunity.com/sharedfiles/filedetails/?id=3174317892)
* [Honkey Kong (3 Laps)](https://steamcommunity.com/sharedfiles/filedetails/?id=3175684498)
