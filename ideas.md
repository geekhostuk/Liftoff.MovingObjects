# Feature Ideas — Liftoff.MovingObjects

A running backlog for the mod, **grouped by system**. Each entry notes what it does, why
it's worth doing, an **effort** estimate, a **status** (`shipped` / `partial` / `open`),
and which files it touches.

Adding an **author-facing option** generally touches three layers:

1. A serialized field in `Liftoff.MovingObjects.Patcher/Patcher.cs` — the injected `MO_*`
   types (`MO_AnimationOptions`, `MO_TriggerOptions`, `MO_Animation`) so it round-trips in
   `TrackBlueprint` XML.
2. The editor UI. The `liftoffui.bundle` can't be rebuilt without Unity, so new controls
   are **added in code** in `AnimationEditorWindow.cs` (see the `Ensure*Controls` methods for
   the pattern) or `PlacementUtilsWindow.cs`.
3. The runtime reader — `Player/AnimationPlayer.cs`, `Player/PhysicsPlayer.cs`, or
   `TriggerBehavior.cs`.

Features that reuse existing data or are pure runtime changes are the cheapest.

> **v1.2.0** shipped the entire open backlog below. Runtime behaviours (continuous motion,
> physics, triggers) and the item-spawn features compile against the current game and are wired
> end-to-end, but should be confirmed with an in-game playtest. A few follow-ons that layer on
> the new item-spawn primitive remain open (see **Remaining follow-ons**).

---

## Shipped

### ✅ Portal-style (seamless) teleport — v1.1.x
Opt-in `seamlessTeleport` flag plus an `exitSpeed` (km/h) override on trigger teleports. Fly
straight through → exit straight; enter at an angle → exit deflected, carrying momentum.
Files: `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`, `Plugin.cs`.

### ✅ High-speed trigger reliability (anti-tunneling) — v1.1.x
Continuous-collision watchdog (`DroneContinuousCollision.cs`) plus swept-ray detection in
`TriggerBehavior.FixedUpdate`, so triggers fire at any speed. Every trigger idea depends on it.

### ✅ Trigger action: (re)start vs. stop — v1.1.2
A `triggerAction` option (`Restart`/`Stop`) lets a trigger halt a running animation
(freeze in place) instead of only starting one.

### ✅ Animation — v1.2.0
- **Easing / curves** (`easingMode`): SmoothStep / EaseIn / EaseOut / EaseInOut remap of the
  per-step lerp. Linear default = unchanged.
- **Ping-pong / yo-yo** (`pingPong`): retraces the visited poses back to the start after the
  forward pass, reusing each hop's timing.
- **Spinner** (`spinnerEnabled` + `spinAxis` + `spinSpeed`): endless procedural rotation driven
  from a gated `Update()` rather than keyframes.
- **Orbit** (`orbitEnabled` + radius/speed/axis + `orbitFacePath`): procedural circular path;
  the authored position is a point on the circle; optional face-along-path; composes with spin.
- **Phase offset** (`phaseOffset` + `randomizePhase`): one-time start delay so a field of
  identical objects desyncs.
- Files: `Patcher.cs`, `Player/AnimationPlayer.cs`, `AnimationEditorWindow.cs`.

### ✅ Physics — v1.2.0
- **Launch impulse** (`launchImpulse` + `launchTorque`, local space): applied when the body goes
  dynamic — catapults, popcorn hazards.
- **Gravity scale / drag / mass** (`overrideGravity` + `gravityScale` + `linearDrag` +
  `angularDrag` + `mass`): custom gravity via a mass-independent `FixedUpdate` force; floaty,
  heavy, or anti-gravity bodies. Defaults preserve existing behaviour.
- **Group physics** *(FINISH)*: grouped objects now simulate as a compound body — only the group
  root gets the `Rigidbody`; `GroupFlags` transform-parents the members underneath it so they act
  as compound colliders. `AddPhysics` enables every group collider; the old guard and the editor
  "CURRENTLY IN DEVELOPEMENT" label are removed.
- Files: `Patcher.cs`, `Player/PhysicsPlayer.cs`, `Plugin.cs`, `AnimationEditorWindow.cs`.

### ✅ Triggers & teleports — v1.2.0
- **One-shot / cooldown** (`triggerOnce` + `triggerCooldown`): gated in `HandlePass`; one-shot
  re-arms per flight via `TriggerBehavior.ResetState` (driven from the drone-reset hook).
- **Sequential vs random targets** (`sequentialTargets`): cycle same-named exit markers in order.
- **Boost / brake gate** (`boostEnabled` + `speedMultiplier` + `targetSpeed`): rescale drone
  speed in place, no teleport. Works standalone (no target needed).
- **Wind / force volume** (`windEnabled` + `forceVector` + `forceMode` + `forceLocalSpace`):
  continuous force while the drone overlaps, via `OnTriggerStay`.
- **Speed-based routing** (`routeBySpeed` + `routeSpeedThreshold`): pick the exit marker by the
  drone's speed band.
- **Sound on trigger** (`playSoundOnTrigger`): drives the native `TrackItemPlaySoundTrigger`
  named as a target (`PlaySoundFile` by reflection). *Needs playtest.*
- **Hazard-on-contact** (`killOnContact`): a `HazardContact` component crashes the drone on
  contact via `FlightManager.CrashDrone` (reflection). *Needs playtest.*
- Files: `Patcher.cs`, `TriggerBehavior.cs`, `HazardContact.cs`, `Plugin.cs`,
  `AnimationEditorWindow.cs`.

### ✅ Editor authoring QoL — v1.2.0
- **8a. Copy/paste MO config**: `Shared.Clipboard` + `Utils/CloneUtils` deep clone; Copy/Paste
  buttons stamp one object's config onto another.
- **8b. Editable / reorderable steps**: per-row update-to-current, move up/down, insert.
- **8c. Animation path preview**: `PathPreview` LineRenderer polyline + orientation ticks.
- **8d. Timeline scrubber**: `AnimationPlayer.SampleAt(normalizedTime)` drives a preview clone.
- **8e. Numeric transform entry + arrow-key nudge**: Pos/Rot X/Y/Z fields on the gizmo; arrows
  nudge by the grid step (Shift = vertical).
- **8g. Trigger/portal validation (lint)**: `EditorUtils.ValidateTriggers` flags dangling
  targets and teleport-with-no-target.
- **8h. Re-enable stats** *(FINISH)*: on-demand "Refresh stats" button (no per-second poll).
- **Trigger-link gizmos**: `TriggerLinkPreview` draws entrance → exit lines + facing arrows.
- Files: `AnimationEditorWindow.cs`, `PlacementUtilsWindow.cs`, `Shared.cs`,
  `Utils/EditorUtils.cs`, `Utils/CloneUtils.cs`, `PathPreview.cs`, `TriggerLinkPreview.cs`.

### ✅ Undo / Redo in the track builder — v1.3.8
Editor-wide undo (`Ctrl+Z`) / redo (`Ctrl+Y`) plus toolbar buttons. Reversible edits are captured
uniformly across native and mod operations by patching the two shared chokepoints —
`TrackEditor.AssignIDToTrackItem` (add) and `TrackEditor.RemoveTrackItem` (remove) — plus the drag
postfixes and numeric transform fields. Edits store deep-cloned `PlacedItem` snapshots keyed by a
runtime-only `MoUndoId` marker (survives the object-identity churn of respawn-on-undo). Mod bulk ops
are wrapped in transactions so each collapses to one entry; native single edits flush per frame;
same-object moves coalesce. A post-load suppression window + large-burst heuristic keep opening a map
from flooding the history. Depth 50; cleared on `TrackEditorGUI.Start`. Config-field edits (detail
pane) are out of scope. Covers object add/delete/move/group only.
- Files: `UndoHistory.cs`, `Utils/MoUndoId.cs`, `Utils/ItemSpawner.cs` (`SpawnExact`), `Plugin.cs`,
  `PlacementUtilsWindow.cs`.

### ✅ Item spawning + duplicate/array/mirror/copy-paste/stamps — v1.2.0
The mod can finally **instantiate a live track item from a `TrackBlueprint`** — the primitive
that blocked every prior copy/paste attempt. The chain (all public, obfuscated types held via
`var`): `TrackEditor.use.GetTrackItemPrefab(itemID)` → `CreateNewTrackItem(prefab)` →
`AssignIDToTrackItem(item)` → `item.ApplyBlueprint(blueprint)`, wrapped in
`Utils/ItemSpawner.cs`. Everything below ships on it via placement-window buttons:
- **Duplicate** and **Array** (single item, offset pattern).
- **Multi-object copy/paste** — copy the enchanted multi-select set, paste relative to the gizmo,
  with fresh `mo_groupId` GUIDs so a pasted group never merges with the source (`Shared.ItemClipboard`).
- **8f. Mirror** — reflect the selection across the vertical plane through the gizmo (position +
  best-effort rotation mirror).
- **Cross-track save/insert** — save a selection to a named stamp file under `mo_stamps/` and
  insert it into any track (`Utils/StampIO.cs`, XmlSerializer over `TrackBlueprint`; all its fields
  are simple/serializable).

*All item-spawn features compile against the game; needs in-game playtest.*

### ✅ Workshop preview override *(FINISH)* — v1.2.0
The `PopupShareContent.ShareItem` patch is restored. `ShareItem` has a single overload, so it is
patched by name alone (no parameter types) — binding without naming the obfuscated third
parameter — and the `Sprite` preview is overridden positionally with a local `preview.png`.

---

## Open — physics objects as first-class participants (requested 2026-07-15, DerHonk83)

Discovered in play: give an object physics and a low mass and you can shoot it around, and even
pick it up with a drone. That makes a ball a plausible *game piece* rather than set dressing — but
today only drones interact with checkpoints, trigger volumes and wind. These four entries are the
gap between "a ball that rolls" and "basketball / pinball / rocket league".

### Checkpoints triggered by physics objects
Let a checkpoint register a physics object/group passing through it, not just a drone. Author-facing
as a pair of opt-ins: a flag on the checkpoint ("physics objects may trigger this") and a flag on the
physics tab ("this object/group can trigger checkpoints"), so neither side surprises the other.
Unlocks scoring volumes — a hoop, a pinball target, a goal.
- **Effort**: medium. The drone-only assumption is baked into the pass detection; needs a second
  detection path plus the anti-tunneling treatment (a fast ball tunnels exactly like a fast drone).
- **Files**: `Patcher.cs` (`MO_TriggerOptions`, `MO_AnimationOptions`), `TriggerBehavior.cs`,
  `AnimationEditorWindow.cs`.
- **Status**: open.

### Trigger volumes triggered by physics objects
Same opt-in as above, extended to the general trigger volumes rather than only checkpoints — so a
ball can fire *any* authored trigger action (sound, teleport, start an animation). Should share one
"who may trigger me" concept with the checkpoint entry rather than growing a second flag.
- **Effort**: small once the checkpoint detection path exists — mostly the same code.
- **Files**: `Patcher.cs`, `TriggerBehavior.cs`, `AnimationEditorWindow.cs`.
- **Status**: open.

### Wind / force volumes apply to physics objects
`windEnabled` currently pushes only drones (`OnTriggerStay` against the drone body). Extend it to
physics objects and groups, applying the force through the body's mass so a heavy crate and a light
ball respond differently — `ForceMode.Force`/`Acceleration` already distinguishes this, and group
bodies have a single root `Rigidbody`, so the compound mass is already correct.
- **Effort**: small-to-medium. The force application is straightforward; the work is identifying
  overlapping MO bodies rather than assuming the drone.
- **Files**: `TriggerBehavior.cs`, `Player/PhysicsPlayer.cs`.
- **Status**: open.

### Sync a physics object between players ("sync between players" checkbox)
The big one. A shared ball needs one authority (host) simulating and broadcasting pose, with other
mod users applying it — everyone currently simulates their own copy, so the ball is solo-only today.
Proposed authoring surface: a per-object/group **"sync between players"** checkbox on the physics tab.
- **Effort**: large, and unlike everything else in this file it needs **transport we don't have** —
  the mod has no networking layer, and the game's own netcode is in the obfuscated assembly. Worth
  scoping against the existing always-on bot room / control backend (JesusMcTwos) before committing:
  if that can relay pose, this becomes tractable; if not, it's a new networking subsystem.
- **Open questions**: authority handover when the host leaves; interpolation/latency handling;
  what happens for non-mod users in the room; whether ownership transfers to whoever last hit the ball.
- **Files**: `Patcher.cs`, `Player/PhysicsPlayer.cs`, `Plugin.cs`, plus a new transport layer.
- **Status**: open — **needs a design spike, not an implementation**.

### Track concepts these unlock
Author-side ideas from the same conversation, listed as motivation rather than work items: a
drone-operated **pinball machine**; a **ski resort** with working cable cars and skiers on the slope
(animation, already shippable today); a **basketball** game (needs checkpoint-on-physics + sync);
**rocket league** (needs all four above).

---

## Remaining follow-ons

The original backlog is shipped. Possible future polish (not currently planned):

- **Mirror axis choice** — mirror is currently across the vertical plane through the gizmo; a
  selector for X/Y/Z mirror planes would generalise it.
- **Stamp browser** — the save/insert flow uses a named stamp field; a list/picker of saved
  stamps under `mo_stamps/` would be friendlier than typing names.
- **True mirrored rotation** — 3D mirroring can't be encoded in a plain `Quaternion` + positive
  scale, so `ItemSpawner.Mirror` uses the standard reflect-forward/up approximation; asymmetric
  items may need manual rotation touch-up after a mirror.
