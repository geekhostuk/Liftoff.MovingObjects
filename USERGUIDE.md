# Liftoff MovingObjects — User Guide

This guide covers every feature of the mod and how to use it, for **track authors**. If you only
want to *fly* maps that use moving objects, you just need the mod installed (see
[Installation](#installation)) — everything below is for building.

> **Beta note (v1.2.10).** Every feature compiles against the current game and is wired end-to-end,
> and the core animation/physics/trigger paths have been playtested in-game. A few of the newest
> runtime paths and item-spawn features (Select-all, Duplicate/Array/Copy-Paste/Mirror/Stamps,
> sound-on-trigger, hazard-on-contact, experimental spectator sync) are guarded so a wrong assumption
> fails gracefully, but confirm them in-game where noted. Please report anything that misbehaves.

---

## Table of contents

- [Installation](#installation)
- [Core concepts](#core-concepts)
- [Units at a glance](#units-at-a-glance)
- [The editor windows & key bindings](#the-editor-windows--key-bindings)
- [Animating an object](#animating-an-object)
- [Physics objects](#physics-objects)
- [Triggers & teleports](#triggers--teleports)
- [Groups](#groups)
- [Placement & construction tools](#placement--construction-tools)
- [Copy/paste an object's configuration](#copypaste-an-objects-configuration)
- [Workshop preview override](#workshop-preview-override)
- [Experimental / config-file options](#experimental--config-file-options)
- [Recipes — worked examples](#recipes--worked-examples)
  - [Animation recipes](#animation-recipes)
  - [Physics recipes](#physics-recipes)
  - [Trigger & teleport recipes](#trigger--teleport-recipes)
  - [Construction & workflow recipes](#construction--workflow-recipes)
- [Debugging your map](#debugging-your-map)
- [Option reference](#option-reference)

---

## Installation

1. Install **BepInEx 5** (the 64-bit Mono build of BepInEx 5.4.x) into your Liftoff folder.
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
- **Defaults are backward-compatible.** Every new option is off / zero by default, so existing maps
  are unaffected. Several zeros are *sentinels* — e.g. Repeats `0` = infinite, Physics time `0` =
  never resets, Mass `0` = immovable, a trigger speed gate of `0` = no gate. Those are called out
  where they matter.

---

## Units at a glance

Every numeric field is in one of these units. The recipes below rely on them, so keep this handy:

| Quantity | Unit |
|----------|------|
| Positions, **Orbit radius**, Pos X/Y/Z | **metres** (Unity world units) |
| Step **Time** / **Delay**, **Warmup**, **Phase offset**, **Cooldown** | **seconds** |
| **Spin speed**, **Orbit speed** | **degrees per second** (deg/s) |
| Rotations, Rot X/Y/Z, step rotation | **degrees** (Euler) |
| **Speed min/max**, **Exit speed**, **Target speed**, **Route threshold** | **km/h** |
| **Launch impulse**, **Launch torque** | applied in the object's **local space** |
| **Gravity scale** | a multiplier — `1` = normal Earth gravity |
| **Mass** | kilograms (`0` = immovable) |
| **Speed multiplier** | a plain factor (`1.5` = +50%, `0.5` = half) |

> A note on the numeric fields: they format to 3 decimals and only accept digits on keypress —
> to enter a negative or decimal value, type the digits and the field will accept a pasted or
> parsed `-`/`.`, or edit the value and let it re-parse. Vectors are three separate X/Y/Z fields.

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
| **F1** | Snap the gizmo to the **Alignment grid** (same as the **Align** button) |
| **F2** | Show/hide the Placement utils window |
| **F3** | Toggle no-clip on the editor fly-camera |
| **F4** | Toggle wireframe view |
| **F5** | **Duplicate selection in place** — a copy lands exactly on the originals as one fresh group; grab and drag it off |
| **F9** | **Delete selection** — removes the whole selection through the editor's own removal, so it's gone from the saved track too |
| **Ctrl+G** | Group / ungroup the current multi-selection (Enhanced editor on) |
| **Middle-click** | Add/remove an object to the multi-selection (Enhanced editor on; nothing else selected) |
| **Shift + Middle-click** | Edit group membership: add a loose object to the selected group, remove a current member, or seed a new group from a lone selection (Enhanced editor on) |
| **Arrow keys** | Nudge the gizmo by the Alignment grid step (**Shift** = vertical); suppressed while typing in a field |

F5 and F9 also have toolbar buttons in the Placement utils window; a pink (grouped) selection
counts as **one** selection, so both hotkeys act on the whole group.

---

## Animating an object

1. Select the item. In the animation editor, set **Type → Animation**.
2. **Add keyframes.** Position/aim the gizmo where you want the object, then click **Add step**.
   Each step records the object's position + rotation and gets a **time** (seconds to move *to*
   this step) and a **delay** (seconds to wait *before* moving). Repeat for each waypoint.
3. **Preview** with the **Play** button (spawns a temporary copy that runs the animation).

The object starts at its **authored pose** (where you placed it) and interpolates from there
through each step in order, then loops.

### Editing steps (non-destructive)
Each step row has buttons:
- **Update** — re-record this step's position/rotation from the current gizmo (fix a waypoint
  without deleting everything after it).
- **↑ / ↓** — reorder the step.
- **+** — insert a new step after this one at the current gizmo pose.
- **delete** — remove the step.

You can also edit the **time** and **delay** fields directly. New steps are created with
`delay = 0, time = 1`.

### Playback options (Animation panel)

| Control | What it does |
|---------|--------------|
| **Teleport to start** | Snap instantly to step 0 instead of gliding into it on the first pass. |
| **Warmup** (s) | One-time delay before the outbound pass each loop (a dwell at the start). |
| **Repeats** | Number of loops; **0 = infinite**. |
| **Trigger action** | `Restart` (a trigger (re)starts it; the object stays dormant until fired) or `Stop` (the object auto-plays from load and a trigger freezes it in place). |
| **Easing** | Curve for each step's motion: `Linear` (default), `SmoothStep`, `EaseIn`, `EaseOut`, `EaseInOut`. Makes pendulums/elevators/doors look natural. |
| **Ping-pong** | After the forward pass, retrace the waypoints back to the start — author half an animation, get the return for free. One there-and-back counts as one repeat. |
| **Phase offset** (s) + **Randomize phase** | One-time start delay so a field of identical objects desyncs instead of moving in lockstep. Randomize picks a delay in `[0, phase offset]`. |
| **Kill drone on contact** | Turns the moving object into a hazard — touching it crashes the drone. |

### Procedural motion (no keyframes needed)
These run continuously and don't need a step list (if either is on, the step list is ignored):

- **Spinner (constant rotation)** — tick **Spinner**, set **Spin axis X/Y/Z** (e.g. `0,1,0` for
  yaw) and **Spin speed (deg/s)**. Great for fan blades, rotating bars, spinning rings. A zero
  axis falls back to yaw (`0,1,0`).
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
| **Time** (Physics time) | How long it stays dynamic before resetting; **0 = run once and never reset**. |
| **Delay** (Physics delay) | Wait before it goes dynamic each cycle. |
| **Warmup** (Physics warmup) | One-time wait before the first cycle. |
| **Launch impulse X/Y/Z** | A one-shot velocity kick applied the instant it goes dynamic, in the object's **local** space — catapults, popcorn hazards. |
| **Launch torque X/Y/Z** | A one-shot spin kick, local space. |
| **Override gravity** | Enable custom gravity. When on, **Gravity scale (1 = normal)** applies: `0` = floats in place, `<1` = floaty, `>1` = heavy, `<0` = anti-gravity (rises). |
| **Linear drag** / **Angular drag** | Air resistance; only applied when > 0 (0 keeps the game defaults). Floaty balloons, slow-settling banners. |
| **Mass** (0 = immovable) | `0` (or ≤ 0) keeps the object effectively immovable (the drone can't shove it); a value > 0 makes it a movable body. |
| **Kill drone on contact** | Same hazard option as animations. |

---

## Triggers & teleports

A trigger lets a **checkpoint** fire behaviour when the drone passes it. Set these in the
**Trigger** section of the animation editor (enable the trigger toggle first). The **Target** and
teleport routing controls only appear on **checkpoint** items; any item can carry a **Name**.

### Naming & targeting
- **Name** — give any object a trigger Name to make it *targetable* (max 16 characters).
- **Target** (checkpoints only) — the Name this checkpoint fires. On pass, every object whose Name
  equals this Target is triggered (animation Restart/Stop, physics start), and — if Teleport is on
  — the drone teleports to a matching **exit marker** (an object with that Name).

### Speed gates
- **Speed min / max (km/h)** — the trigger only fires if the drone's speed is within the band.
  Leave at 0 for "no gate" (each bound is independent — set just a min, just a max, or both).

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
  **Speed multiplier** (e.g. `1.5` boost, `0.5` brake) or an absolute **Target speed (km/h)**
  (Target speed wins when it's > 0). Works on a standalone checkpoint (no target needed).

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
2. **Middle-click** each object to select it (they highlight magenta). Clicking any one object of
   an existing group selects the **whole group** (it turns pink) as one real selection.
3. Press **Ctrl+G** to group them (Ctrl+G again on a grouped object ungroups it).
4. Put the animation/physics config on the **group root** (the object you author the motion on);
   the rest follow it.

**Editing membership without rebuilding:** with a group (or any item) selected, **Shift +
Middle-click** a loose object to **add** it to the group, or Shift+MMB a current member to
**remove** it. Shift+MMB from a lone selection seeds a new group. This grows or shrinks a group
without re-selecting everything.

Grouped **animation** and grouped **physics** are both supported — for physics the root gets the
single rigidbody and the others become compound colliders, so they collide as one body.

---

## Placement & construction tools

All of these live in the **Placement utils** window (F2).

### Grid & precision
- **Alignment grid** (Grid field) + **Align** button (or **F1**) — snap the gizmo to a grid of
  this size. **Default 0.5.** Also drives arrow-key nudge, Duplicate/Array spacing, and paste
  snapping.
- **Placement grid** (drag grid) — grid size applied while dragging items. **Default 0.0 = off.**
- **Pos X/Y/Z**, **Rot X/Y/Z** — type exact transform values for the selected gizmo; they also
  track the gizmo live.
- **Arrow keys** — nudge the gizmo by the Alignment grid step (**Shift** = up/down).

### Multi-selection (used by all the copy/mirror/stamp tools)
With **Enhanced editor** on and nothing selected in the normal editor, **middle-click** objects to
build a selection set (magenta highlight); middle-click again to remove one; click empty space to
clear. Clicking one member of a group selects the whole group. If you have no multi-selection, the
copy/mirror/save tools fall back to the single selected item.

**Select all objects** — the **Select all objects** button marks every placed item on the map at
once, so you can copy, mirror, save-to-stamp, delete (F9), or grab and move the whole track in one
action. Existing groups are left as-is: move everything with each sub-group still intact, or press
**Ctrl+G** to weld the lot into a single group. It catches every object on a freshly loaded track,
including blocks you've never moused over — though a never-hovered block, while fully selected, may
not turn magenta until the game builds its hover highlight (that art is cosmetic; the operations act
on the whole selection regardless).

### Duplicate / array / mirror / delete
- **Duplicate item** — clone the selected item one grid step over, carrying its full MO config
  under a fresh group id.
- **Array count** (default **3**) + **Array item** — stamp N duplicates, each a further grid step
  along.
- **Duplicate selection in place (F5)** — copy the whole selection (a pink group counts as one)
  exactly on top of the originals, as a fresh group. Grab it and drag it off — no detour via the
  gizmo/origin.
- **Delete selection (F9)** — remove every selected item through the editor's own removal, so
  they're gone from the saved track.
- **Mirror selection** — spawn a mirrored copy of the selection across the vertical plane through
  the gizmo. Position is exact; rotation is a best-effort mirror (3D mirroring can't be encoded
  perfectly, so asymmetric items may need a manual rotation touch-up).

### Multi-object copy/paste
- **Copy selection** — copy the whole selected set (with all their MO config) to a clipboard.
- **Paste selection** — drop the set relative to the gizmo/cursor. Any paste/duplicate/stamp of
  **more than one item arrives as a single fresh group** (under fresh group ids, so a pasted group
  never merges into the source). The paste translation is snapped to the grid step, so pieces keep
  their alignment.

### Reusable stamps (share sections across tracks)
- **Stamp name** (default **"stamp"**) — a name for the saved stamp.
- **Save selection to stamp** — write the selected set to `<Liftoff>/mo_stamps/<name>.xml`.
- **Insert stamp** — load that stamp and paste it into the current track at the gizmo (as one
  group). Build a library of reusable "asset stamps" and share the files.

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

## Experimental / config-file options

The mod reads one setting from its BepInEx config file (`BepInEx/config/Liftoff.MovingObjects.cfg`,
created on first run). Edit it with the game closed, or via a BepInEx config manager.

| Section | Key | Default | What it does |
|---------|-----|---------|--------------|
| `[Experimental]` | `SpectatorAnimationSync` | `false` | When you **spectate another pilot** in multiplayer, your client never receives their drone-reset events, so moving objects drift out of sync with what the spectated pilot sees. Turning this on watches the game log for the spectator camera re-attaching to a pilot and re-syncs the moving objects on that pilot's reset (and when you switch spectate target). Best-effort and Liftoff-version-specific; network latency can still cause brief clipping. **Takes effect on the next game start.** |

Leave it off unless you're specifically chasing spectator desync — it depends on a log line that
varies by Liftoff version.

---

## Recipes — worked examples

Each recipe below is a concrete combination of settings that produces a specific effect. Enter the
values shown; `·` separates fields on one line. Tune the numbers to taste — the ranges under
**Tune** are starting points. Field names match the labels in the editor exactly.

### Animation recipes

#### Rotating fan blade
> A blade/ring that spins on the spot forever.

`Type: Animation` · `Spinner (constant rotation): on` · `Spin axis X/Y/Z: 0, 1, 0` · `Spin speed (deg/s): 180`

**Tune:** 20–30 = a lazy sign, 180 = a visibly turning fan, 720+ = a blurred rotor. No steps needed.

#### Spinning hazard rotor
> A fast rotor that crashes the drone on contact.

`Type: Animation` · `Spinner: on` · `Spin axis X/Y/Z: 0, 1, 0` · `Spin speed (deg/s): 540` · `Kill drone on contact: on`

**Tune:** point the axis along the blade's mount — `1,0,0` or `0,0,1` for a rotor that sweeps vertically across a gate.

#### Slow rotating billboard / sign
> A gently turning display or logo.

`Type: Animation` · `Spinner: on` · `Spin axis X/Y/Z: 0, 1, 0` · `Spin speed (deg/s): 25`

#### Barber-pole / rolling log
> Rotation about a horizontal axis instead of yaw.

`Type: Animation` · `Spinner: on` · `Spin axis X/Y/Z: 0, 0, 1` · `Spin speed (deg/s): 120`

**Tune:** use `1,0,0` to roll about the other horizontal axis; combine with a physics-free platform to make a "rolling drum" obstacle.

#### Swinging pendulum
> A blade or wrecking ball that swings side to side and slows at each extreme.

Place the object at **one extreme** (this authored pose is the start). Then:

`Type: Animation` · add **one step** at the **opposite extreme** (`time ≈ 1.5`) · `Easing: EaseInOut` · `Ping-pong: on` · `Repeats: 0`

**Why:** ping-pong retraces the single hop, so it swings extreme→extreme→extreme forever; EaseInOut slows it at both ends like a real pendulum. Shorter `time` = faster swing.

#### Elevator / rising platform
> A lift that rises, dwells, and returns, forever.

Place the platform at the **bottom**. Then:

`Type: Animation` · add **one step** at the **top** (`time ≈ 2`) · `Easing: SmoothStep` · `Ping-pong: on` · `Warmup: 1.5` · `Repeats: 0`

**Tune:** Warmup adds a one-time dwell at the bottom each loop; add a per-step **Delay** on the top step to dwell at the top too.

#### Timed gap-filler platform
> A sliding platform you line up to open/close a gap at a precise moment.

`Type: Animation` · 2–3 steps across the gap · `Easing: Linear`

**How:** use **Toggle path preview** to see the trajectory, then drag the **Scrub** slider to find the exact frame the platform blocks/clears the gap. Add a **Phase offset** so it's in the right phase when a pilot arrives on a given lap.

#### Sliding door that opens on a checkpoint
> A door that stays shut until the drone passes a gate, then opens once.

On the door: `Type: Animation` · step 1 = **closed** pose, step 2 = **open** pose · `Trigger action: Restart` · Trigger **Name:** `door1`
On a checkpoint upstream: enable Trigger · **Target:** `door1`

**Why:** with `Restart`, a targeted object stays **dormant** until fired, so the door sits closed until the checkpoint triggers it.

#### Moving platform a gate switches OFF
> A hazard/platform that runs from the start and a checkpoint freezes.

On the object: `Type: Animation` (spinner or steps) · `Trigger action: Stop` · Trigger **Name:** `rotor1`
On a checkpoint: enable Trigger · **Target:** `rotor1`

**Why:** `Stop` mode runs from load and the trigger **freezes it in place** (it doesn't snap back). A drone reset restarts it from the top.

#### Orbiting object
> An object that circles a point (a moon, a patrolling drone).

`Type: Animation` · `Orbit (circular path): on` · `Orbit radius: 5` · `Orbit speed (deg/s): 60` · `Orbit axis X/Y/Z: 0, 1, 0` · `Face along path: on`

**Note:** the object's placed position is a point *on* the circle, so there's no jump at start. 60 deg/s = one lap every 6 s.

#### Orbit + spin combo
> An object that circles *and* rotates as it goes.

Orbit as above, **plus** `Spinner: on` · `Spin axis X/Y/Z: 0, 1, 0` · `Spin speed (deg/s): 200`

**Why:** the spinner composes on top of the orbit's facing — good for a tumbling satellite or a rotating ring that also travels.

#### Desynced field of identical objects
> A grid of identical movers that don't all move in lockstep.

Author the motion once, then Array/paste copies (see construction recipes). On each copy (or on the config you paste): `Phase offset (s): 2` · `Randomize phase: on`

**Why:** each object waits a random `[0, 2] s` before starting, so the field ripples instead of pulsing as one.

### Physics recipes

#### Collapsing bridge / falling debris
> Objects that drop away when the run starts (or on a cycle).

`Type: Physics` · `Time: 0` (fall once, never reset) · `Delay: 0.5`

**Tune:** stagger a row by giving each piece a slightly larger **Delay** (0.3, 0.6, 0.9…) for a ripple collapse. Set **Time** > 0 to have them reset and drop again on a cycle.

#### Catapult / launch pad
> An object flung upward the instant it activates.

`Type: Physics` · `Launch impulse X/Y/Z: 0, 8, 0` · `Delay: 0`

**Note:** impulse is in **local space**, so it follows the object's orientation — tilt the object to aim the launch. Bigger Y = higher throw.

#### Popcorn / erupting hazard
> Debris that bursts upward, tumbling, and kills on contact.

`Type: Physics` · `Launch impulse X/Y/Z: 0, 6, 0` · `Launch torque X/Y/Z: 2, 1, 0` · `Kill drone on contact: on`

**Tune:** vary impulse/torque per copy so a cluster erupts chaotically.

#### Floaty balloon / drifting banner
> A light object that sinks slowly and settles.

`Type: Physics` · `Override gravity: on` · `Gravity scale (1 = normal): 0.2` · `Linear drag: 1`

**Why:** low gravity scale + some linear drag makes it drift down gently instead of dropping (drag only applies when > 0).

#### Anti-gravity riser
> An object that rises instead of falling.

`Type: Physics` · `Override gravity: on` · `Gravity scale (1 = normal): -0.3`

**Tune:** more negative = faster ascent; add **Linear drag** to cap the rise speed.

#### Zero-g floater
> An object that hangs exactly where it's dropped.

`Type: Physics` · `Override gravity: on` · `Gravity scale (1 = normal): 0`

**Tip:** add a small **Launch impulse** for a slow, straight drift through space.

#### Heavy wrecking ball
> A weighty body the drone can nudge but not fling.

`Type: Physics` · `Mass (0 = immovable): 50` · `Override gravity: on` · `Gravity scale (1 = normal): 1.5`

**Contrast:** the default `Mass 0` is **immovable** (the drone bounces off). Any Mass > 0 opts into a movable body — small values shove easily, large values barely budge.

#### Rolling boulder (grouped)
> A ball made of parts that rolls as one solid body.

Group the halves/parts (Enhanced editor → middle-click → **Ctrl+G**). On the **group root**:
`Type: Physics` · `Mass (0 = immovable): 20`

**Note:** grouped physics builds a single compound body on the root, and mesh colliders on a physics body are forced convex so it actually rolls (rather than falling through). Preview with **Play**.

### Trigger & teleport recipes

#### Basic teleport portal
> Pass a gate, appear at a marker.

Exit marker (any object): Trigger **Name:** `portalA`
Entrance (checkpoint): enable Trigger · **Target:** `portalA` · `Teleport: on`

#### Seamless momentum portal
> Portal-style teleport that carries your speed and heading.

As above, plus on the entrance: `Seamless teleport: on` · `Exit speed: 0`
Rotate the **exit marker** to aim where you come out.

**Why:** `Exit speed 0` keeps your entry speed; fly straight through → exit straight along the marker's forward; enter at an angle → exit deflected.

#### Portal that also boosts
> A seamless portal that spits you out faster.

Seamless portal as above, but on the entrance: `Exit speed: 120`

**Why:** `Exit speed > 0` keeps the computed direction but overrides the magnitude — a portal that's also a launch pad (or set it below your entry speed for a braking portal).

#### Speed-boost pad (no teleport)
> Fly through a gate, get faster — no teleport.

Checkpoint (standalone, no target): enable Trigger · `Boost / brake gate: on` · `Speed multiplier: 1.5`

#### Brake gate
> A gate that slows you down.

Checkpoint: enable Trigger · `Boost / brake gate: on` · `Speed multiplier: 0.5`

#### Absolute speed setter
> Clamp the drone to an exact speed on pass.

Checkpoint: enable Trigger · `Boost / brake gate: on` · `Target speed (km/h): 100`

**Note:** Target speed (when > 0) **overrides** Speed multiplier.

#### Speed-gated trigger
> A trigger that only fires within a speed window.

On any trigger: `Speed min: 80` · `Speed max: 150`

**Why:** the trigger ignores passes below 80 or above 150 km/h. Leave either at `0` for "no gate on that end" — e.g. min 120, max 0 = "only fires at 120+".

#### Skill-gated shortcut
> Fast pilots get sent to a different exit than slow ones.

Two exit markers sharing **Name:** `route1` (place them where each path should resume).
Entrance (checkpoint): **Target:** `route1` · `Teleport: on` · `Route by speed: on` · `Route threshold (km/h): 130`

**Why:** below 130 → first marker (safe path), at/above 130 → second marker (the shortcut).

#### Predictable multi-exit portal
> Several exits, taken in order rather than at random.

Multiple exit markers sharing one **Name**. Entrance checkpoint: **Target:** that name · `Teleport: on` · `Sequential targets: on`

**Why:** each pass cycles to the next marker in order instead of picking one at random.

#### Wind tunnel / updraft
> A zone that continuously pushes the drone while it's inside.

Checkpoint: enable Trigger · `Wind / force volume: on` · `Force X/Y/Z: 0, 15, 0` · `Force mode: Acceleration`

**Tune:** `Acceleration` pushes every drone the same regardless of weight; `Force` is weight-dependent. Turn on `Force in local space` and rotate the checkpoint to make a slanted wind or a side-draft that follows the gate's facing.

#### One-shot event
> A trigger that fires only the first time each flight.

On any trigger: `Trigger once (per flight): on`

**Note:** it re-arms automatically when the drone resets.

#### Rate-limited trigger
> A trigger that can't spam-fire.

On any trigger: `Cooldown (s): 3`

#### Sound on trigger
> Play a sound when a checkpoint fires.

Checkpoint: enable Trigger · **Target:** `boom` · `Play sound on trigger: on`
Place a **Play Sound** track item and give *it* Trigger **Name:** `boom` (and pick its sound).

**Why:** the checkpoint drives the native sound item that shares its Target name.

### Construction & workflow recipes

#### Picket fence / colonnade
> A row of evenly spaced copies.

Select one post. Set `Array count: 8` · click **Array item**.

**Tune:** each copy lands one **Alignment grid** step further along — raise the Grid value for wider spacing, then re-array.

#### Duplicate and drag off
> Quickly clone a built assembly.

Select it (a group counts as one). Press **F5** (Duplicate selection in place) → the copy lands on top as a fresh group → drag it to its new home.

#### Symmetric gate
> Mirror one half of a structure to build the other.

Build one side, select it, position the **gizmo** on the centre line, click **Mirror selection**.

**Note:** position mirrors exactly; rotation is best-effort, so touch up the rotation on any asymmetric pieces afterward.

#### Reusable asset stamp
> Save a section to reuse across tracks.

Multi-select the section. Set **Stamp name:** `archway` · click **Save selection to stamp**
(writes `<Liftoff>/mo_stamps/archway.xml`). In any track, click **Insert stamp** to drop it in as
one group. Share the `.xml` file with other authors.

#### Multi-part door group
> Make several panels move as one, and tweak membership later.

Enhanced editor on → **middle-click** each panel → **Ctrl+G** to group. Author the motion on the
**root** panel. Later, **Shift + Middle-click** a panel to add/remove it from the group without
rebuilding the selection.

---

## Debugging your map

Two tools in the Placement utils window help you catch problems before flying:

- **Validate triggers** — lints all triggers and prints one of:
  - `Dangling target '<target>' on <itemID>: no object has that trigger Name.` — a checkpoint
    targets a name nothing carries (a teleport/trigger that goes nowhere).
  - `Teleport enabled with no target on <itemID>.` — a teleport with an empty Target.
  - `Info: name '<name>' used by <count> objects (ok for multi-exit / multi-trigger).` — a
    duplicate name; fine for multi-exit portals or firing many objects at once.
  - `No trigger issues found.` — all clear.
- **Toggle trigger links** — draws a green line from each trigger entrance to every matching exit
  marker, with a red facing arrow, so you can *see* your portal wiring. Re-toggle after moving
  objects to refresh the lines.

---

## Option reference

### Animation options
| Option | Type | Notes |
|--------|------|-------|
| Teleport to start | bool | Snap to step 0 on first pass |
| Warmup | float (s) | Once per loop (outbound only) |
| Repeats | int | 0 = infinite |
| Trigger action | Restart / Stop | Restart = dormant until fired; Stop = runs from load, trigger freezes it |
| Easing | enum | Linear / SmoothStep / EaseIn / EaseOut / EaseInOut |
| Ping-pong | bool | Retrace back to start; there-and-back = one repeat |
| Spinner | bool + axis + speed(deg/s) | Continuous rotation; zero axis = yaw |
| Orbit | bool + radius(m) + speed(deg/s) + axis + face-path | Continuous circular path; placed pos is on the circle |
| Phase offset | float (s) + randomize | One-time desync delay in [0, offset] |
| Kill drone on contact | bool | Hazard |

### Physics options
| Option | Type | Notes |
|--------|------|-------|
| Time / Delay / Warmup | float (s) | Time 0 = run once, never resets |
| Launch impulse / torque | vector (local) | One-shot on going dynamic |
| Override gravity + gravity scale | bool + float | 1 = normal, 0 = float, <0 = anti-grav |
| Linear / angular drag | float | Applied only when > 0 |
| Mass | float (kg) | 0 = immovable; > 0 = movable body |
| Kill drone on contact | bool | Hazard |

### Trigger options
| Option | Type | Notes |
|--------|------|-------|
| Name / Target | string (≤16) | Name-matching backbone; Target is checkpoint-only |
| Speed min / max | float (km/h) | 0 = no gate (each bound independent) |
| Teleport | bool | Move drone to exit marker |
| Seamless teleport + Exit speed | bool + float(km/h) | Portal momentum; exit 0 = keep speed |
| Trigger once / Cooldown | bool / float(s) | Re-fire control |
| Sequential targets | bool | Cycle exit markers in order |
| Route by speed + threshold | bool + float(km/h) | Speed-gated routing (below→first, ≥→second) |
| Boost / brake + multiplier / target speed | bool + float / float(km/h) | In-place speed rescale; target speed wins when >0 |
| Wind / force + vector + mode + local | bool + vector + Force/Acceleration + bool | Continuous force inside the volume |
| Play sound on trigger | bool | Drives native sound item by Name |

---

*Credit for the original mod goes to [ps-hek](https://github.com/ps-hek/Liftoff.MovingObjects);
this fork keeps it working against current Liftoff builds and adds the v1.2.x feature set.*
