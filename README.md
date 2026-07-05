# Liftoff MovingObjects Mod

> **Warning!** This project is not official and is not supported by the Liftoff game developers.

> **Warning!** Maps built with this mod must be flown with the mod installed. Without the mod, animations and physics on those maps will not run.

A Liftoff mod that adds animated and physics-driven track objects, plus the editor extensions used to author them.

## What this is for

Most people who install this mod do so to **fly community-made maps that use moving objects** rather than to author new ones. The animation, physics, and trigger code in the plugin reads metadata that mappers embed in `TrackBlueprint` items, then attaches runtime components that drive the motion at flight time. With the mod installed, those maps animate; without it, the same maps load but everything stays still.

If you've been pointed at this mod by a community or league, you most likely just want the runtime — see [Install](#install) below.

If you want to **build** maps with moving objects, see the **[User Guide](USERGUIDE.md)** — it details every feature and how to use it.

### Communities using this mod

- **[JMT FPV](https://jmtfpv.com)** runs a multiplayer room called **JMT-MOD** that races on tracks built around moving objects from this mod. JMT FPV publishes their own setup walkthrough at <https://jmtfpv.com/install> — follow that for the JMT-specific server/lobby steps; the *mod-side* installation below is the same regardless of which community you're flying with.

## About this fork

This is a [geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects) of [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects). **All credit for the mod itself goes to [ps-hek](https://github.com/ps-hek)** — they designed it, built it, authored the patcher and editor windows, and shipped the maps community has been racing on. This fork exists only to keep the mod working: the upstream release was last published in early 2024 and stopped working against current Liftoff builds (Unity 2022.3, BepInEx 5.4.23). The prebuilt 1.0.14 plugin would load but objects on modded maps would no longer animate. v1.1.0 in this fork restores the runtime against the current game.

If you're looking for the original project, the commit history of new-feature work, or want to file an issue against the design rather than the modernization, please go to [ps-hek/Liftoff.MovingObjects](https://github.com/ps-hek/Liftoff.MovingObjects).

### New in 1.2.4 (beta)

Follow-ups to the 1.2.3 copy/paste fix, from in-game playtest:

- **Pasted / mirrored objects keep their MO config and grouping.** Paste and mirror were writing the
  mod's config onto a throwaway blueprint that the game's spawn path ignores, so pasted objects lost
  their animation, trigger, and (most visibly) their **group** — a copied group came back as loose
  single objects. The `mo_*` config is now written onto each spawned item's own blueprint, so a
  pasted group is a group again and animated/triggered objects keep behaving.
- **Paste lands on the grid.** A multi-object paste anchored its centroid on the gizmo, which for an
  even-count selection sits half a cell off-grid — so the paste dropped slightly out of alignment.
  The paste translation is now snapped to the grid step (respects the grid setting; no-op when the
  grid is off), so pasted pieces keep their alignment.

### New in 1.2.3 (beta)

- **Multi-object copy/paste and mirror now keep their layout.** A pasted (or mirrored) selection
  used to collapse every object onto the cursor and lose its rotation, because the paste read each
  object's *blueprint* position/rotation — fields the game only syncs to the object at track
  save/load, so mid-edit they're stale. Copy/mirror now capture each item's **live** transform
  (position, rotation, scale) and set it explicitly on the spawned object, so the pasted set
  reproduces the exact relative spacing, orientation, and scale around the gizmo. Save-to-stamp
  likewise now records live positions rather than stale ones.
- **Editor triangle stats no longer run on window open.** The per-second stats poll that caused
  editor stutter was disabled back in 1.2.0, but a later change re-ran the scene-wide triangle count
  every time the placement window opened — a fresh hitch on large maps, and a whole-scene poly count
  that looked wrong for a selection. Stats are now computed **only** when you click *Refresh stats*.

> Note: these changes are compile-verified and follow the editor's existing live-transform idiom,
> but should be confirmed with an in-game playtest — paste/mirror a multi-item selection and check
> the relative spacing and rotations survive.

### New in 1.2.2 (beta)

- **Spinner / orbit now run in flight, not just in the editor.** Continuous-rotation and
  circular-orbit objects that had *no keyframe steps* previewed correctly in the editor but were
  never given a runtime player in flight — injection only fired for objects with at least one
  animation step, and a spinner/orbit needs none. Spinner- or orbit-only objects now animate in
  flight the same way they always did in the editor preview.
- **Experimental spectator animation sync (opt-in, off by default).** When you spectate another
  pilot in multiplayer, your client never receives the local drone-reset events, so moving objects
  keep running from their own start time and drift out of sync with what the spectated pilot sees.
  A new **`[Experimental] SpectatorAnimationSync`** config option watches the game log for the
  spectator camera re-attaching to a pilot and re-syncs the moving objects on that pilot's reset
  (and when you switch spectate target). It is best-effort and Liftoff-version-specific — network
  latency can still cause brief clipping — and is based on a proof-of-concept contributed by
  **[AMPW-german](https://github.com/AMPW-german)**, reworked here to run on the Unity main thread,
  behind a config flag, from a single non-threaded log subscription (instead of the original's
  cross-thread call and four Harmony log-sink patches).

> Note: as with 1.2.0, these changes compile and are wired end-to-end but should be confirmed with
> an in-game playtest. The spinner-in-flight fix is compile-verified only, and the experimental
> spectator sync depends on a log line that varies by Liftoff version — treat it as beta.

### New in 1.2.1

- **Grouped-sphere physics fix** — a group of two half-spheres (or any grouped physics body) now
  collides and rolls as one solid object. Mesh colliders on a physics body are forced convex
  (Unity silently drops non-convex meshes from a dynamic rigidbody, so the body would fall through
  or slide instead of roll), and the **F2 physics preview** now builds a real compound body instead
  of just making the other members visually follow the root — so the preview matches in-flight.
- **Fully tested** — verified end-to-end both in-game (flight) and in the track builder.

### New in 1.2.0

A large feature release implementing the remaining `ideas.md` backlog. Grouped by system:

- **Animation** — easing curves (SmoothStep / ease in/out), ping-pong / yo-yo playback, a
  spinner (constant-rotation) mode, an orbit / circular-path mode, and a per-object phase offset
  (with randomize) to desync fields of identical objects.
- **Physics** — an initial launch impulse/torque, and gravity-scale / drag / mass overrides
  (floaty, heavy, or anti-gravity bodies). **Grouped physics now works** (compound body on the
  group root).
- **Triggers & teleports** — one-shot / cooldown gates, sequential vs random exit markers,
  boost / brake gates (in-place speed rescale), wind / force volumes, speed-based routing,
  sound-on-trigger (drives the native sound item), and hazard-on-contact (kills the drone).
- **Editor authoring** — copy/paste of MO config between objects, editable / reorderable
  animation steps, an animation path preview, a timeline scrubber, numeric transform entry +
  arrow-key nudge, trigger/portal validation (lint), on-demand object/triangle stats, and
  in-editor trigger-link gizmos.
- **Item spawning** — the mod can now instantiate track items from blueprints (the long-missing
  primitive), enabling single-item **Duplicate** and **Array** placement. Multi-object
  copy/paste, mirror, and save-to-file build on the same primitive.
- **Workshop preview override re-enabled** — sharing a track again honours a local `preview.png`.

> Note: the continuous/physics/trigger runtime behaviours and the item-spawn features compile
> against the current game and are wired end-to-end, but should be confirmed with an in-game
> playtest.

### New in 1.1.2

- **[Trigger action: (re)start vs. stop](#trigger-action-restart-vs-stop)** — a `Restart`/`Stop` option on animations, so a trigger can now switch a running animation *off* (freeze in place), the mirror of the existing "start on trigger" behavior.
- Verified against **Liftoff 1.7.4**.

### New in 1.1.1

- **[Portal-style teleports](#portal-style-teleports)** — opt-in seamless teleports that carry the drone's momentum and orientation into the exit gate's frame, with an optional exit-speed override.
- **[High-speed trigger reliability](#high-speed-trigger-reliability)** — continuous-collision watchdog plus swept-ray detection so triggers no longer get tunnelled through by a fast drone.

### What's verified working in v1.1.0

- Object animation, physics, and triggers on community maps (e.g. *Honkey Kong*).
- Flying through the JMT-MOD multiplayer room.
- Track editor extensions (animation editor window, placement utilities) for authors.
- Animations correctly re-bind after switching tracks within a session.

### Resolved in 1.2.0

- **Workshop preview overwrite (`PopupShareContent.ShareItem`) is working again.** `ShareItem` has a single overload, so it is now patched by name alone (no parameter types), which binds without needing to name the obfuscated third-parameter type; the `Sprite` preview is reached positionally.
- **Editor "Object count" / "Triangle count" no longer pinned to `0`.** Counting is restored behind an on-demand "Refresh stats" button (plus one count on window open) instead of the per-second poll that stuttered on large maps; triangle counts use `GetIndexCount` to avoid per-mesh allocations.

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

### Portal-style teleports

Teleport triggers can now carry your momentum through the gate, *Portal*-style, instead of dropping you out with your old world-space velocity (which left you exiting sideways). With **Seamless teleport** enabled, the drone's entry velocity and orientation are re-expressed in the exit marker's frame:

- Fly straight through → exit straight along the exit gate's forward.
- Enter at an angle → exit deflected by the same angle, carrying your speed.

An optional **Exit speed** (km/h) overrides the exit magnitude while keeping the computed direction: `0` preserves your entry speed (true momentum), `> 0` turns the portal into a launch pad or a brake gate.

**Authoring:** the exit marker is any object given a trigger `Name` — rotate it to aim the exit. The entrance is a checkpoint with trigger `Target` + `Teleport` + `Seamless teleport` (plus an optional `Exit speed`). It's opt-in and defaults off, so existing maps are unaffected.

### High-speed trigger reliability

Triggers (teleports, animation/physics starts) previously **missed fast passes** — Unity's `OnTriggerEnter` only fires when the drone overlaps a collider on a physics step, so a fast drone could tunnel straight through. The mod now adds a continuous-collision watchdog on the drone plus swept-ray path detection against trigger colliders, so triggers fire reliably at any speed.

### Trigger action: (re)start vs. stop

A trigger could previously only **(re)start** an animation, and any object that was a trigger target began **dormant** and played on trigger. Animations now carry a **Trigger action** option — `Restart` (default) or `Stop`:

- **Restart** — unchanged behavior: the object stays dormant until a trigger (re)starts it.
- **Stop** — the object **runs from the start** and a trigger **freezes it in place** (it does not snap back to its start pose). This is the mirror of "start on trigger" — e.g. a spinning hazard or moving platform that a gate switches off. A drone reset restarts the animation from the top.

**Authoring:** in the animation editor window, set **Trigger action** to `Stop`, give the object a trigger `Name`, and target that name from a checkpoint's trigger `Target`. It defaults to `Restart`, so existing maps are unaffected.

## Install

If you only want to play modded maps, this is all you need.

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your Liftoff folder. (Specifically, the 64-bit Mono build of BepInEx 5.4.x.)
2. Download `Liftoff.MovingObjects-1.2.4.zip` from the [latest release](https://github.com/geekhostuk/Liftoff.MovingObjects/releases/latest).
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

## Roadmap

Planned and proposed features live in [`ideas.md`](ideas.md) — a backlog grouped by system
(animation, physics, triggers/teleports, editor QoL, infrastructure), each entry tagged with
a status (`shipped` / `partial` / `open`) and effort estimate. It's the place to look before
picking up new work or filing a feature request.

## Maps (sampler)

* [MOD_PSHEK_FRPV_BANDO-NICE_1](https://steamcommunity.com/sharedfiles/filedetails/?id=3174317892)
* [Honkey Kong (3 Laps)](https://steamcommunity.com/sharedfiles/filedetails/?id=3175684498)

## Credits

- **[ps-hek](https://github.com/ps-hek)** — original author and maintainer of the mod, the patcher, the editor UI, and the published maps. Everything user-facing about this mod is their work.
- **[geekhostuk](https://github.com/geekhostuk)** — maintains this fork, modernizing the mod to keep working with newer Liftoff builds.
- **[AMPW-german](https://github.com/AMPW-german)** — contributed the proof-of-concept for multiplayer spectator animation sync (v1.2.2).

If you find this mod useful, please go give the upstream repo a star.
