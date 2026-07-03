# Liftoff MovingObjects — User Guide

This guide covers every feature of the mod and how to use it, for **track authors**. If you only
want to *fly* maps that use moving objects, you just need the mod installed (see
[Installation](#installation)) — everything below is for building.

> **Beta note (v1.2.0).** Every feature compiles against the current game and is wired end-to-end,
> but the runtime behaviours (continuous motion, physics, triggers) and the item-spawn features
> (Duplicate/Array/Copy-Paste/Mirror/Stamps) have not all been playtested in-game yet. New runtime
> code is guarded so a wrong assumption fails gracefully. Please report anything that misbehaves.

---

## Table of contents

- [Installation](#installation)
- [Core concepts](#core-concepts)
- [The editor windows & key bindings](#the-editor-windows--key-bindings)
- [Animating an object](#animating-an-object)
- [Physics objects](#physics-objects)
- [Triggers & teleports](#triggers--teleports)
- [Groups](#groups)
- [Placement & construction tools](#placement--construction-tools)
- [Copy/paste an object's configuration](#copypaste-an-objects-configuration)
- [Workshop preview override](#workshop-preview-override)
- [Option reference](#option-reference)

---

## Installation

1. Install **BepInEx 5** into your Liftoff folder.
2. Unzip the release so `BepInEx/plugins/Liftoff.MovingObjects.dll` and
   `BepInEx/patchers/Liftoff.MovingObjects.Patcher.dll` sit inside the game's existing `BepInEx`
   folder.
3. Launch Liftoff. Maps that use these features **must be flown with the mod installed** — without
   it the maps load but nothing moves.

---

## Core concepts

- **It attaches behaviour to existing track items.** You place normal track items in the Liftoff
  editor, then use the mod's windows to give them animation, physics, or trigger behaviour. That
  configuration is saved *inside the track file* (as `mo_*` metadata on each item).
- **An item is one of three types:** `None` (a normal static item), `Animation` (keyframe or
  procedural motion), or `Physics` (a rigidbody that falls/launches). You pick this with the
  **Type** dropdown in the animation editor.
- **Triggers work by name.** Any item can be given a trigger **Name**. A checkpoint can be given a
  trigger **Target**; when the drone passes it, everything whose Name matches that Target fires
  (starts/stops an animation, teleports the drone, etc.). This name-matching is the backbone of
  teleports, remote animation triggers, and sound-on-trigger.
- **Speeds are in km/h**; angular speeds in **degrees per second**.
- **Defaults are backward-compatible.** Every new option is off / zero by default, so existing maps
  are unaffected.

---

## The editor windows & key bindings

The mod adds two windows to the track editor.

### Animation editor (per-item)
Opens in the item detail pane when you **select a single item** in the editor. It holds the Type
dropdown, the Trigger section, and the Animation/Physics option panels for that item.

### Placement utils window
A floating panel for map-wide tools (grid, selection, stats, spawning). Toggle it with **F2**.

### Key bindings (in the editor)

| Key | Action |
|-----|--------|
| **F1** | Snap the gizmo to the **Alignment grid** |
| **F2** | Show/hide the Placement utils window |
| **F3** | Toggle no-clip on the editor fly-camera |
| **F4** | Toggle wireframe view |
| **Ctrl+G** | Group / ungroup the current multi-selection |
| **Middle-click** | Add/remove an object to the multi-selection (Enhanced editor on; nothing else selected) |
| **Arrow keys** | Nudge the gizmo by the Alignment grid step (**Shift** = vertical); suppressed while typing in a field |

---

## Animating an object

1. Select the item. In the animation editor, set **Type → Animation**.
2. **Add keyframes.** Position/aim the gizmo where you want the object, then click **Add step**.
   Each step records the object's position + rotation and gets a **time** (seconds to move *to*
   this step) and a **delay** (seconds to wait *before* moving). Repeat for each waypoint.
3. **Preview** with the **Play** button (spawns a temporary copy that runs the animation).

The object interpolates from its start pose through each step in order, then loops.

### Editing steps (non-destructive)
Each step row has buttons:
- **Update** — re-record this step's position/rotation from the current gizmo (fix a waypoint
  without deleting everything after it).
- **↑ / ↓** — reorder the step.
- **+** — insert a new step after this one at the current gizmo pose.
- **delete** — remove the step.

You can also edit the **time** and **delay** fields directly.

### Playback options (Animation panel)

| Control | What it does |
|---------|--------------|
| **Teleport to start** | Snap instantly to step 0 instead of gliding into it on the first pass. |
| **Warmup** (s) | One-time delay before the animation begins each loop. |
| **Repeats** | Number of loops; **0 = infinite**. |
| **Trigger action** | `Restart` (a trigger (re)starts it) or `Stop` (a trigger freezes it in place; the object auto-plays from load so there's something to stop). |
| **Easing** | Curve for each step's motion: `Linear` (default), `SmoothStep`, `EaseIn`, `EaseOut`, `EaseInOut`. Makes pendulums/elevators/doors look natural. |
| **Ping-pong** | After the forward pass, retrace the waypoints back to the start — author half an animation, get the return for free. |
| **Phase offset** (s) + **Randomize phase** | One-time start delay so a field of identical objects desyncs instead of moving in lockstep. Randomize picks a delay in `[0, phase offset]`. |
| **Kill drone on contact** | Turns the moving object into a hazard — touching it crashes the drone. |

### Procedural motion (no keyframes needed)
These run continuously and don't need a step list:

- **Spinner (constant rotation)** — tick **Spinner**, set **Spin axis X/Y/Z** (e.g. `0,1,0` for
  yaw) and **Spin speed (deg/s)**. Great for fan blades, rotating bars, spinning rings.
- **Orbit (circular path)** — tick **Orbit**, set **Orbit radius**, **Orbit speed (deg/s)**, and
  **Orbit axis X/Y/Z**. The object's authored position becomes a point on the circle (no jump at
  start). Tick **Face along path** to make it aim along its direction of travel. A spinner can be
  layered on top of an orbit.

### Authoring previews
- **Toggle path preview** — draws the trajectory as a cyan line through the steps with yellow
  facing ticks, so you can see the path without pressing Play. Updates live as you edit steps.
- **Scrub** slider — scrub the animation to any point in time to find the exact frame a platform
  blocks a gap (drives a preview clone). The preview captures the steps when you first drag it;
  reselect the item to pick up later step edits.

---

## Physics objects

1. Select the item, set **Type → Physics**.
2. Configure when it "drops", and optionally how it launches and how gravity treats it.
3. **Play** to preview.

A physics object starts frozen (kinematic), then goes dynamic and falls under gravity.

| Control | What it does |
|---------|--------------|
| **Physics time** | How long it stays dynamic before resetting; **0 = run forever** (never resets). |
| **Physics delay** | Wait before it goes dynamic each cycle. |
| **Physics warmup** | One-time wait before the first cycle. |
| **Launch impulse X/Y/Z** | A one-shot velocity kick applied the instant it goes dynamic, in the object's **local** space — catapults, popcorn hazards. |
| **Launch torque X/Y/Z** | A one-shot spin kick, local space. |
| **Override gravity** | Enable custom gravity. When on, **Gravity scale (1 = normal)** applies: `0` = floats in place, `<1` = floaty, `>1` = heavy, `<0` = anti-gravity. |
| **Linear drag** / **Angular drag** | Air resistance; only applied when > 0 (0 keeps the game defaults). Floaty balloons, slow-settling banners. |
| **Mass** | `0` keeps the object effectively immovable (the drone can't shove it); a value > 0 makes it a movable body. |
| **Kill drone on contact** | Same hazard option as animations. |

---

## Triggers & teleports

A trigger lets a **checkpoint** fire behaviour when the drone passes it. Set these in the
**Trigger** section of the animation editor (enable the trigger toggle first).

### Naming & targeting
- **Name** — give any object a trigger Name to make it *targetable*.
- **Target** (checkpoints only) — the Name this checkpoint fires. On pass, every object whose Name
  equals this Target is triggered (animation Restart/Stop, physics start), and — if Teleport is on
  — the drone teleports to a matching **exit marker** (an object with that Name).

### Speed gates
- **Speed min / max (km/h)** — the trigger only fires if the drone's speed is within the band.
  Leave at 0 for "no gate".

### Teleports
- **Teleport** — teleport the drone to an exit marker (an object carrying the target Name). With
  several same-named markers, one is chosen (see routing below).
- **Seamless teleport** — *portal-style*. Instead of dropping the drone at the marker with its
  world velocity, re-express its entry velocity and orientation in the exit marker's frame: fly
  straight through → exit straight along the marker's forward; enter at an angle → exit deflected,
  carrying momentum. Rotate the exit marker to aim the exit.
- **Exit speed** (km/h) — with seamless on, `0` preserves entry speed (true momentum); `> 0`
  overrides the speed while keeping the computed direction (a portal that's also a launch/brake).

### Re-fire control
- **Trigger once (per flight)** — fires only the first pass; re-arms when the drone resets.
- **Cooldown (s)** — minimum time before it can fire again.

### Routing (when several exit markers share a Name)
- **Sequential targets** — cycle through the markers in order (predictable multi-exit portals)
  instead of the default random pick.
- **Route by speed** + **Route threshold (km/h)** — below the threshold take the first marker,
  at/above it take the second (skill-gated shortcuts).

### Speed pads without teleporting
- **Boost / brake gate** — rescale the drone's speed on pass *without* teleporting. Set a
  **Speed multiplier** (e.g. `1.5` boost, `0.5` brake) or an absolute **Target speed (km/h)**.
  Works on a standalone checkpoint (no target needed).

### Environmental force
- **Wind / force volume** — apply a continuous force to the drone *while it's inside* the
  checkpoint volume. Set **Force X/Y/Z**, a **Force mode** (`Acceleration` = same push regardless
  of drone weight; `Force` = weight-dependent), and optionally **Force in local space** to make the
  direction follow the checkpoint's orientation. Updrafts, wind tunnels, push/pull zones.

### Sound
- **Play sound on trigger** — drives the game's native sound-trigger item. Place a
  **Play Sound** track item, give *it* a trigger **Name** equal to this checkpoint's **Target**,
  and its configured sound plays when the checkpoint fires.

---

## Groups

Group several items so they move together as one compound object (multi-part doors, articulated
arms, debris clusters).

1. Turn on **Enhanced editor** (Placement utils window).
2. **Middle-click** each object to select it (they highlight magenta).
3. Press **Ctrl+G** to group them (Ctrl+G again on a grouped object ungroups it).
4. Put the animation/physics config on the **group root** (the object you author the motion on);
   the rest follow it.

Grouped **animation** and grouped **physics** are both supported — for physics the root gets the
single rigidbody and the others become compound colliders, so they collide as one body.

---

## Placement & construction tools

All of these live in the **Placement utils** window (F2).

### Grid & precision
- **Alignment grid** + **Align** button (or **F1**) — snap the gizmo to a grid of this size.
- **Placement grid** — grid size applied while dragging items.
- **Pos X/Y/Z**, **Rot X/Y/Z** — type exact transform values for the selected gizmo; they also
  track the gizmo live.
- **Arrow keys** — nudge the gizmo by the Alignment grid step (**Shift** = up/down).

### Multi-selection (used by all the copy/mirror/stamp tools)
With **Enhanced editor** on and nothing selected in the normal editor, **middle-click** objects to
build a selection set (magenta highlight); middle-click again to remove one; click empty space to
clear. If you have no multi-selection, the copy/mirror/save tools fall back to the single selected
item.

### Duplicate / array / mirror
- **Duplicate item** — clone the selected item one grid step over, carrying its full MO config
  under a fresh group id.
- **Array count** + **Array item** — stamp N duplicates, each a further grid step along.
- **Mirror selection** — spawn a mirrored copy of the selection across the vertical plane through
  the gizmo. Position is exact; rotation is a best-effort mirror (3D mirroring can't be encoded
  perfectly, so asymmetric items may need a manual rotation touch-up).

### Multi-object copy/paste
- **Copy selection** — copy the whole selected set (with all their MO config) to a clipboard.
- **Paste selection** — drop the set relative to the gizmo/cursor. Grouped items stay grouped but
  under **fresh group ids**, so a pasted group never merges into the source.

### Reusable stamps (share sections across tracks)
- **Stamp name** — a name for the saved stamp.
- **Save selection to stamp** — write the selected set to `<Liftoff>/mo_stamps/<name>.xml`.
- **Insert stamp** — load that stamp and paste it into the current track at the gizmo. Build a
  library of reusable "asset stamps" and share the files.

### Diagnostics
- **Refresh stats** — update the **Object count** / **Triangle count** in the Statistics section
  (on demand, so it doesn't stutter the editor).
- **Validate triggers** — lint the map: warns about a Target with no matching Name, and
  teleport-with-no-target; reports duplicate names as info (they're fine for multi-exit portals).
- **Toggle trigger links** — draw a green line from each trigger entrance to every matching exit
  marker, with a red arrow for the exit's facing, so portal links are visible without flying.
  Re-toggle after moving objects to refresh.

---

## Copy/paste an object's configuration

Separate from copying the *objects*, you can copy just the **MO settings** between existing items:

- **Copy MO config** (animation editor) — copy the selected item's animation options, steps, and
  trigger options.
- **Paste MO config** — stamp them onto another selected item. Author one moving gate, paste onto
  twenty.

---

## Workshop preview override

When you share a track to the Workshop, the mod overrides the preview image with a local file if
present: put a **`preview.png`** in your Liftoff root folder and it's used as the shared track's
preview.

---

## Option reference

### Animation options
| Option | Type | Notes |
|--------|------|-------|
| Teleport to start | bool | Snap to step 0 on first pass |
| Warmup | float (s) | Once per loop |
| Repeats | int | 0 = infinite |
| Trigger action | Restart / Stop | Stop freezes in place |
| Easing | enum | Linear / SmoothStep / EaseIn / EaseOut / EaseInOut |
| Ping-pong | bool | Retrace back to start |
| Spinner | bool + axis + speed(deg/s) | Continuous rotation |
| Orbit | bool + radius + speed(deg/s) + axis + face-path | Continuous circular path |
| Phase offset | float (s) + randomize | One-time desync delay |
| Kill drone on contact | bool | Hazard |

### Physics options
| Option | Type | Notes |
|--------|------|-------|
| Physics time / delay / warmup | float (s) | time 0 = forever |
| Launch impulse / torque | vector (local) | One-shot on going dynamic |
| Override gravity + gravity scale | bool + float | 1 = normal, 0 = float, <0 = anti-grav |
| Linear / angular drag | float | Applied only when > 0 |
| Mass | float | 0 = immovable |
| Kill drone on contact | bool | Hazard |

### Trigger options
| Option | Type | Notes |
|--------|------|-------|
| Name / Target | string | Name-matching backbone |
| Speed min / max | float (km/h) | 0 = no gate |
| Teleport | bool | Move drone to exit marker |
| Seamless teleport + Exit speed | bool + float(km/h) | Portal momentum; 0 = keep speed |
| Trigger once / Cooldown | bool / float(s) | Re-fire control |
| Sequential targets | bool | Cycle exit markers |
| Route by speed + threshold | bool + float(km/h) | Speed-gated routing |
| Boost / brake + multiplier / target speed | bool + float / float(km/h) | In-place speed rescale |
| Wind / force + vector + mode + local | bool + vector + Force/Acceleration + bool | Continuous force inside |
| Play sound on trigger | bool | Drives native sound item by Name |

---

*Credit for the original mod goes to [ps-hek](https://github.com/ps-hek/Liftoff.MovingObjects);
this fork keeps it working against current Liftoff builds and adds the v1.2.0 feature set.*
