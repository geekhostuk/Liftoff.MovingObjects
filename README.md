# Liftoff MovingObjects Mod

> **Warning!** This project is not official and is not supported by the Liftoff game developers.

> **Note:** Only maps that actually *use* the mod's runtime features — moving objects, wind, physics
> changes, triggers — must be flown with the mod installed; without it those maps load but the
> animations and physics won't run. Maps you build with the editor's quality-of-life additions alone
> (no moving objects) are ordinary Liftoff tracks and play fine without the mod.

A Liftoff mod that adds animated and physics-driven track objects, plus the editor extensions used to
author them.

## What this is for

Most people who install this mod do so to **fly community-made maps that use moving objects** rather
than to author new ones. The animation, physics, and trigger code in the plugin reads metadata that
mappers embed in `TrackBlueprint` items, then attaches runtime components that drive the motion at
flight time. With the mod installed, those maps animate; without it, the same maps load but everything
stays still.

If you've been pointed at this mod by a community or league, you most likely just want the runtime —
see [Install](#install) below. If you want to **build** maps with moving objects, see the
**[User Guide](USERGUIDE.md)** — it details every feature and how to use it.

### Communities using this mod

- **[JMT FPV](https://jmtfpv.com)** runs a multiplayer room called **JMT-MOD** that races on tracks
  built around moving objects from this mod. JMT FPV publishes their own setup walkthrough at
  <https://jmtfpv.com/install> — follow that for the JMT-specific server/lobby steps; the *mod-side*
  installation below is the same regardless of which community you're flying with.

### Leaderboards on modded tracks

Historically, runs flown with this mod installed were withheld from Liftoff's leaderboards — the
injected mod trips the game's anti-cheat. That gate is on **Liftoff's side**, not something this mod
controls.

**Lugus (Liftoff's developer) has now enabled leaderboard times for modded tracks** through their
backend, so runs on maps that use this mod can post times again — confirmed working in-game (thanks
to Jan at Lugus for the change, and to Honk for verifying it). Nothing in this mod changed as a
result; it's a server-side update on Liftoff's end.

## About this fork

This is a [geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects) of
[ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects). **All credit for the
mod itself goes to [ps-hek](https://github.com/ps-hek)** — they designed it, built it, authored the
patcher and editor windows, and shipped the maps community has been racing on. This fork exists only
to keep the mod working: the upstream release was last published in early 2024 and stopped working
against current Liftoff builds (Unity 2022.3, BepInEx 5.4.23). v1.1.0 in this fork restored the
runtime against the current game, and the v1.2.x–v1.3.x line adds the full moving-objects feature set.

If you're looking for the original project or want to file an issue against the design rather than the
modernization, please go to [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects).

## What's new in 1.3.9

- **Editor performance fix** — the 1.3.8 undo/redo feature could hitch badly on large maps because it
  scanned the whole scene on every edit. Object resolution is now cached, so editing stays smooth no
  matter how many objects the map has.
- **Multiplayer spectator sync is now on by default** — a spectated pilot's moving objects re-align
  each time they reset, out of the box. Still a toggle (`[Multiplayer] SpectatorAnimationSync`).
- Recent releases also added **Undo / Redo in the track builder** (**Ctrl+Z** / **Ctrl+Y**,
  editor-wide) in 1.3.8, and author-set **Show-Text durations** plus a **mod-version compatibility
  gate** in 1.3.7.

See the **[full changelog](CHANGELOG.md)** for the complete release history.

## Features

Each feature is documented in full in the **[User Guide](USERGUIDE.md)**; this is the tour.

### Object animation
Step-by-step keyframe animation plus procedural motion (spinner, orbit) for objects, authored via the
in-game animation editor window — with easing, ping-pong, phase offsets, and triggerable start/stop.

![Animation demo](images/animation.gif)

### Physics
Turn an object into a rigidbody that falls, launches, or floats — with launch impulse/torque, gravity
overrides, drag, and mass. Grouped physics simulates as one compound body.

![Physics demo](images/physics.gif)

### Triggers & teleports
Name-matched triggers let a checkpoint fire behaviour on pass: portal-style seamless teleports (with
momentum), boost/brake gates, wind/force volumes, speed gates and routing, sound-on-trigger, and
hazard-on-contact. A continuous-collision watchdog keeps triggers reliable at any speed.

### Unlock blueprint objects
Place objects from the Blueprint map on any map.

![Blueprint objects demo](images/blueprint.png)

### Editor authoring tools
Multi-selection, groups, duplicate/array/mirror, multi-object copy/paste, reusable cross-track
stamps, numeric transform entry, path/scrub previews, trigger-link gizmos, a trigger linter, and
editor-wide undo/redo. See the [User Guide](USERGUIDE.md) for the full toolkit and key bindings.

## Install

If you only want to play modded maps, this is all you need.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your Liftoff folder.
   (Specifically, the 64-bit Mono build of BepInEx 5.4.x.)
2. Download `Liftoff.MovingObjects-1.3.9.zip` from the
   [latest release](https://github.com/geekhostuk/Liftoff.MovingObjects/releases/latest).
3. Extract the zip into your Liftoff install folder (the one that contains `Liftoff.exe`). It writes:
   - `BepInEx/plugins/Liftoff.MovingObjects.dll`
   - `BepInEx/patchers/Liftoff.MovingObjects.Patcher.dll`
4. Launch Liftoff. Modded maps should now animate.

If you're flying with JMT FPV, follow <https://jmtfpv.com/install> for the lobby/server side after the
mod is installed.

## Building from source

Only needed if you're modifying the mod or rebuilding against a newer game version. Requires the
**.NET SDK 10** and Liftoff installed via Steam:

```powershell
./build.ps1            # build only
./build.ps1 -Deploy    # build and copy into BepInEx
```

See **[CONTRIBUTING.md](CONTRIBUTING.md)** for the full build pipeline, the project architecture, the
injected `MO_*` track schema, and the `tools/PatchHelper` IL-inspection toolkit.

## Roadmap

Planned and proposed features live in [`ideas.md`](ideas.md) — a backlog grouped by system, each entry
tagged with a status (`shipped` / `partial` / `open`) and an effort estimate. It's the place to look
before picking up new work or filing a feature request.

## Maps (sampler)

* [MOD_PSHEK_FRPV_BANDO-NICE_1](https://steamcommunity.com/sharedfiles/filedetails/?id=3174317892)
* [Honkey Kong (3 Laps)](https://steamcommunity.com/sharedfiles/filedetails/?id=3175684498)

## Credits

- **[ps-hek](https://github.com/ps-hek)** — original author and maintainer of the mod, the patcher,
  the editor UI, and the published maps. Everything user-facing about this mod is their work.
- **[geekhostuk](https://github.com/geekhostuk)** — maintains this fork, modernizing the mod to keep
  working with newer Liftoff builds.
- **[AMPW-german](https://github.com/AMPW-german)** — contributed the proof-of-concept for
  multiplayer spectator animation sync (v1.2.2).

If you find this mod useful, please go give the upstream repo a star.
