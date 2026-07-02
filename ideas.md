# Feature Ideas — Liftoff.MovingObjects

A running backlog for the mod, **grouped by system**. Each entry notes what it does, why
it's worth doing, an **effort** estimate, a **status** (`shipped` / `partial` / `open`),
and which files it touches.

Adding an **author-facing option** generally touches three layers:

1. A serialized field in `Liftoff.MovingObjects.Patcher/Patcher.cs` — the injected `MO_*`
   types (`MO_AnimationOptions`, `MO_TriggerOptions`, `MO_Animation`) so it round-trips in
   `TrackBlueprint` XML.
2. The editor UI. The `liftoffui.bundle` can't be rebuilt without Unity, so new controls
   are **added in code** in `AnimationEditorWindow.cs` (see the seamless-teleport controls
   for the pattern) or `PlacementUtilsWindow.cs`.
3. The runtime reader — `Player/AnimationPlayer.cs`, `Player/PhysicsPlayer.cs`, or
   `TriggerBehavior.cs`.

Features that reuse existing data or are pure runtime changes are the cheapest.

---

## Shipped

### ✅ Portal-style (seamless) teleport
**Status: shipped** (branch `JMT01`). Opt-in `seamlessTeleport` flag plus an `exitSpeed`
(km/h) override on trigger teleports.

- **What it does:** Instead of just moving the drone and keeping its world-space velocity
  (which makes you exit flying sideways), a seamless teleport re-expresses the drone's entry
  velocity and orientation in the destination's frame. Fly straight through → exit straight
  along the exit gate's forward; enter at an angle → exit deflected by the same angle,
  carrying momentum. Just like *Portal*.
- **Exit speed:** `0` preserves entry speed (true momentum); `> 0` overrides the magnitude
  while keeping the computed direction — turning a portal into a launch pad or brake gate.
- **Authoring:** Exit marker = object with trigger `Name`; rotate it to aim the exit.
  Entrance = checkpoint with trigger `Target` + `Teleport` + `Seamless teleport`
  (+ optional `Exit speed`).
- **Math:** `entryToExit = dst.rotation * inverse(src.rotation)`, applied to both velocity
  and rotation (`TriggerBehavior.cs:126-138`).
- **Backward compatible:** defaults off, so existing maps are unaffected.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`, `Plugin.cs`.

### ✅ High-speed trigger reliability (anti-tunneling)
**Status: shipped** (branch `JMT01`).

- **What it does:** Triggers previously **missed fast passes** — Unity's `OnTriggerEnter`
  only fires when the drone overlaps the collider on a physics step, so a fast drone tunnels
  straight through. Fixed by (a) a continuous-collision watchdog that re-asserts
  `ContinuousSpeculative` on the drone every frame and records its path
  (`DroneContinuousCollision.cs`), and (b) manual **swept-ray detection** of the drone's
  prev→current segment against trigger colliders in `TriggerBehavior.FixedUpdate`
  (`TriggerBehavior.cs:161-203`).
- **Why it matters:** Guarantees triggers fire at any speed — every trigger-based idea below
  depends on this.
- **Files:** `DroneContinuousCollision.cs`, `TriggerBehavior.cs`, `Plugin.cs`.

---

## Animation

Current behavior: keyframe steps interpolated with **linear `Lerp`** for both position and
rotation (`AnimationPlayer.cs:75-77`); supports warmup delay, per-step delay, and N-repeat
or infinite looping (`animationRepeats`, 0 = infinite). No easing, no procedural motion.

### Trigger action: (re)start vs. stop  *(NEW, logic — requested)*
- **Status: open.** **Effort: low.**
- **What:** Today a trigger can only make an animation **(re)start**. An object that's a
  trigger target always begins **dormant** and plays on trigger; there's no way to have an
  animation that runs **from the start** and then **stops** when triggered. Add a
  `triggerAction` option — `Restart` (current behavior) or `Stop` — so authors can choose
  what a trigger does to the animation.
- **Why:** Requested by a mapper. Enables the mirror of the existing pattern: instead of
  "dormant → start on trigger", you get "running → stop on trigger" (e.g. a spinning hazard
  or moving platform that a gate switches off). Reuses the trigger plumbing that already
  exists; it's a small dispatch + option.
- **How it changes the current behavior:**
  - `Trigger()` currently always calls `Restart(true)` (`AnimationPlayer.cs:101-104`). It
    becomes a switch on `triggerAction`: `Restart` keeps today's behavior; `Stop` halts the
    running coroutine.
  - Being a trigger target currently *forces* dormancy —
    `waitForTrigger = !string.IsNullOrEmpty(options.triggerName)` (`Plugin.cs:265`) suppresses
    the auto-start in `AnimationPlayer.Start` (`:30`). This must be **decoupled**: a `Stop`-mode
    object still registers its `TriggerName` but auto-plays on load (`waitForTrigger = false`),
    so the trigger has something running to stop. `Restart`-mode targets stay dormant as now.
  - **Design nuance — freeze vs. reset:** the existing `Stop()` *resets the object to its
    start pose* (`AnimationPlayer.cs:106-119`). "Stop on trigger" most likely means **freeze
    in place**, so this needs a `StopAtCurrent()` that stops the coroutine *without* snapping
    back. (A reset-on-stop variant could be added later.)
  - **Backward compatible:** `triggerAction` defaults to `Restart` (enum value `0`), so every
    existing map is unaffected.
- **Possible extensions (out of scope):** a `Toggle` action (start if stopped, stop if
  running) and the same option for physics objects (`PhysicsPlayer.Trigger`).
- **Files:** `Patcher.cs` (`MO_AnimationOptions`: `triggerAction` int enum),
  `AnimationEditorWindow.cs` (code-added dropdown, like the seamless-teleport controls),
  `AnimationPlayer.cs` (`Trigger` dispatch + `StopAtCurrent`), `Plugin.cs`
  (`AddAnimation`/`AddTrigger`: compute `waitForTrigger` from the action, pass it through).

### Easing / animation curves
- **Status: open.** **Effort: low.**
- **What:** Steps interpolate with raw linear `Lerp`, which looks robotic. Add a `smooth`
  flag (or an enum of curves) that swaps in an ease — e.g. `Mathf.SmoothStep` on `t`.
- **Why:** Pendulums, elevators, doors, and platforms instantly look far better. Roughly a
  one-line math change behind an option.
- **Files:** `Patcher.cs` (`MO_AnimationOptions`), `AnimationEditorWindow.cs`,
  `AnimationPlayer.cs` (the `MoveObject` lerp).

### Ping-pong / yo-yo animations
- **Status: open.** **Effort: low-moderate.**
- **What:** A `pingPong` option that plays steps forward then in reverse, instead of looping
  back to step 0.
- **Why:** Mappers author *half* an animation (e.g. a gate opening) and get the return for
  free. Great for oscillating hazards.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `AnimationPlayer.cs` (reverse the
  cached steps on alternate passes in the loop).

### Spinner / constant-rotation mode  *(NEW, flashy)*
- **Status: open.** **Effort: low.**
- **What:** Author an axis + angular speed (deg/s or RPM) for endless rotation, instead of
  authoring dozens of tiny keyframes. Fan blades, rotating bars, spinning rings.
- **Why:** The single most common "moving hazard" is a spinner, and it's currently painful
  to build by hand. A dedicated mode is trivial to drive and reads cleanly in the editor.
- **Files:** `Patcher.cs` (`MO_AnimationOptions`: axis + speed), `AnimationEditorWindow.cs`,
  `AnimationPlayer.cs` (a rotate branch that ignores the step list).

### Orbit / circular path  *(NEW, flashy)*
- **Status: open.** **Effort: moderate.**
- **What:** Procedural circular/elliptical motion around a center point + radius (+ optional
  tilt), as an alternative to keyframes — optionally facing along the path.
- **Why:** Smooth loops (orbiting gates, carousels, swinging arcs) are tedious to approximate
  with linear steps. One parametric path looks better and is easier to author than many
  keyframes.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `AnimationPlayer.cs`.

### Phase offset / random start delay  *(NEW, flashy — pairs with groups)*
- **Status: open.** **Effort: low.**
- **What:** A per-object start offset (or a "randomize" flag) so a field of identical
  pistons/platforms desyncs instead of moving in lockstep.
- **Why:** Lockstep motion looks artificial; a phase spread instantly makes a row of hazards
  feel organic. Reuses the existing `animationWarmupDelay` plumbing.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `AnimationPlayer.cs`.

---

## Physics

Current behavior: an initially-kinematic rigidbody (`mass = float.MaxValue` so the drone
can't shove it) goes dynamic and falls under **gravity only** — no initial velocity, forces,
or per-object tuning (`PhysicsPlayer.cs:22,39`). Loop control via `simulatePhysicsTime`
(0 = forever), `simulatePhysicsDelay`, and `simulatePhysicsWarmupDelay`.

### Initial launch impulse  *(flashy)*
- **Status: open.** **Effort: moderate.**
- **What:** Physics objects only drop under gravity today. Add a configurable launch
  velocity/direction applied when the body goes dynamic.
- **Why:** Catapults, swinging hazards, "popcorn" obstacles — far more dynamic than gravity
  drops.
- **Files:** `Patcher.cs` (`MO_AnimationOptions`), `AnimationEditorWindow.cs`,
  `PhysicsPlayer.cs` (apply velocity at the un-kinematic point, `:39`).

### Gravity scale / drag override  *(NEW, flashy)*
- **Status: open.** **Effort: low-moderate.**
- **What:** A per-object gravity multiplier plus linear/angular drag.
- **Why:** Floaty balloons, low-gravity debris, slow-settling banners, buoyancy-like drift —
  a lot of visual variety from a couple of rigidbody fields.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `PhysicsPlayer.cs` (set
  `useGravity`/`drag`/custom gravity on the rigidbody).

### Group physics  *(FINISH — half-built)*
- **Status: partial.** **Effort: moderate-high.** *Biggest "finish-it" item.*
- **What:** Grouped objects can't simulate physics together — the path is disabled with a
  TODO (`Plugin.cs:230`; UI shows "CURRENTLY IN DEVELOPEMENT" for groups). The `FakeGroup`
  infrastructure (`Utils/FakeGroup.cs`) already builds a parent/child hierarchy for grouped
  *animations*; extend that to physics.
- **Why:** Compound moving hazards (multi-part doors, articulated arms, debris clusters) need
  it, and the scaffolding is already there.
- **Files:** `Plugin.cs` (`AddPhysics`/`GroupFlags`), `Utils/FakeGroup.cs`,
  `PhysicsPlayer.cs`.

---

## Triggers & teleports

Current behavior: triggers match by `triggerName`/`triggerTarget`, fire all matching
`AnimationPlayer`/`PhysicsPlayer.Trigger()`s, and optionally teleport. Speed gates
(`triggerMinSpeed`/`triggerMaxSpeed`, km/h) are optional. Multiple exit markers → a
**random** one is chosen (`TriggerBehavior.cs:117`). `_triggered` blocks re-firing only
until the drone exits the volume — there's no cooldown or one-shot.

### One-shot / cooldown triggers  *(logic)*
- **Status: open.** **Effort: low.**
- **What:** A `triggerOnce` bool (fires only the first pass per flight) and/or a re-arm
  cooldown (seconds before the trigger can fire again).
- **Why:** One-time events (a single drop, a one-shot teleport) and rate-limiting. We're
  already in this code path, so it's a small guard/timer addition.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`
  (`HandlePass`).

### Sequential vs random teleport targets  *(logic)*
- **Status: open.** **Effort: low.**
- **What:** When several exit markers share a name, teleport currently picks one at
  **random**. Add a `sequential` option to cycle through them in order.
- **Why:** Predictable multi-exit portals / routing. Random is already a feature (scatter
  portal); this just adds the deterministic alternative.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs` (a counter
  instead of `Random.Range` at `:117`).

### Boost / brake gate  *(NEW, flashy + logic)*
- **Status: open.** **Effort: low.**
- **What:** Rescale the drone's speed on pass **without** teleporting — a non-teleport reuse
  of the `exitSpeed` remap already written for portals (`TriggerBehavior.cs:130-134`).
  A multiplier (×1.5 boost, ×0.5 brake) or absolute km/h target.
- **Why:** Speed pads and brake gates are classic track elements and the math already exists;
  this just applies it in place instead of at a teleport destination.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`.

### Wind / force volume  *(NEW, flashy)*
- **Status: open.** **Effort: moderate.**
- **What:** Apply a continuous force/acceleration to the drone **while it's inside** the
  trigger volume — updrafts, wind tunnels, push/pull zones.
- **Why:** Adds a whole class of environmental gameplay. The per-frame detection path already
  exists in `TriggerBehavior.FixedUpdate`; this adds a "still inside" force application
  rather than a one-shot pass event.
- **Files:** `Patcher.cs` (force vector + mode), `AnimationEditorWindow.cs`,
  `TriggerBehavior.cs`.

### Speed-based routing  *(NEW, logic)*
- **Status: open.** **Effort: moderate.**
- **What:** Choose different exit markers by the drone's speed band (slow → exit A, fast →
  exit B), building on the existing speed gates + multi-target selection.
- **Why:** Skill-gated routing and shortcuts — reward (or punish) carrying speed through a
  portal.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`.

### Sound on trigger  *(re-scoped, flashy)*
- **Status: open.** **Effort: moderate** (was moderate-high).
- **What:** Play a sound on trigger / teleport / animation start.
- **Why:** Big immersion boost — portals and moving hazards feel real with audio cues.
- **Cheaper path:** the game already ships a native **`TrackItemPlaySoundTrigger`** (seen in
  `Utils/EditorUtils.cs:23-29`). Driving that existing item from a trigger likely avoids
  bundling/loading custom audio entirely — investigate before building an `AudioSource`
  pipeline from scratch.
- **Files:** `TriggerBehavior.cs` (+ possibly `Patcher.cs`/`AnimationEditorWindow.cs` for a
  clip reference); audio loading only if the native trigger can't be reused.

### Hazard-on-contact for moving objects  *(NEW, flashy)*
- **Status: open.** **Effort: moderate.**
- **What:** Let an animated/physics object kill or penalize the drone on contact, e.g. by
  driving the game's `TrackItemKillDroneTrigger`.
- **Why:** Turns moving platforms into genuine hazards (swinging blades, crushing walls)
  rather than just scenery.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs` /
  `Utils/EditorUtils.cs`.

---

## Editor & authoring QoL

**Current editor state for context:** grid snap (F1/button), drag snap, noclip (F3),
wireframe (F4), "enchanted" middle-click selection + Ctrl+G grouping, and the
animation/physics/trigger panels. UI is UIElements loaded from `liftoffui.bundle`; new
controls must be **code-added** (no Unity rebuild). Key gaps: animation **steps are
display-only** (each row shows position/rotation as labels with only time/delay + a delete
button — no edit/reorder), stats are disabled, and there's no copy/paste, path preview, or
validation.

Note the two *different* "copy/paste" asks below: **8a** copies MO *settings* between objects
that already exist (feasible today, pure C#); the **Multi-object copy/paste** flagship below
copies the *objects themselves* (needs the item-spawn spike). They're independent — 8a can
ship first as a stepping stone.

### Multi-object copy/paste + cross-track save/insert  *(NEW, flagship — most-requested)*
- **Status: open.** **Effort: moderate-high** (gated on one research spike).
- **Requested by:** track builders across the community (via honk). pshek attempted this
  several times but couldn't reach deep enough into Liftoff's internals — this note pins down
  *why* and *where the wall is*, so the next attempt starts from the right place.
- **What:** Select several objects (the enchanted-editor multi-select already does this), then
  **copy** and **paste** the whole set — the actual track items, not just their settings —
  preserving each item's type, transform, scale, and all `mo_*` config, positioned relative to
  the cursor/gizmo. Then the natural extension: **save a selection to a file and insert it into
  another track** (reusable "asset stamps" / a prefab library shared between maps).
- **Why it's the biggest win:** every builder rebuilds symmetric and repetitive sections by
  hand today. Real multi-object copy/paste (and cross-track insert) is the single largest
  authoring time-saver, and it unblocks 8f (duplicate/array/mirror) which is the same
  primitive with an offset pattern.
- **Why it kept failing — the actual wall:** the mod has **never spawned a track item.**
  Everything it does is *attach components to flags that already exist* (`AnimationPlayer`,
  `TriggerBehavior`), reparent them for grouping (`Plugin.GroupFlags`), or clone an object's
  *highlight overlay* for selection (`PlacementUtilsWindow.Highlight`). Creating a genuine new
  `TrackItem*` with its own `TrackBlueprint` is a capability that doesn't exist yet. That
  missing spawn primitive — not the selection or the data — is what blocked every prior try.
- **Why it's reachable now (three enablers already in place):**
  1. **Multi-select is solved.** Enchanted middle-click tags a set with `GroupSelectionInfo`
     markers and each object's full `TrackBlueprint` is reachable via
     `ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>` (`PlacementUtilsWindow.cs:245-278`).
  2. **Blueprints already serialize to XML** — that's how the `mo_*` fields persist in track
     files. So a copied selection is just a list of blueprints, and **save/insert is writing
     that list to a file and reading it back**. A saved stamp is effectively a mini track file.
  3. **The game already instantiates items from blueprints** — that's how it loads a track.
     Candidates found in `Assembly-CSharp.dll`: the load path (`TrackEditorStateLoadTrack`,
     `RoutineLoadTrack`, `RoutinePreloadTrackItems`) and the palette-spawn path
     (`itemSpawnerContentPanel`, `lastSpawnedPrefab`, `StoreBlueprint` / `storeInBlueprint`).
- **The one spike:** locate and drive (reflection/Harmony) the game's *"instantiate a live
  item from a `TrackBlueprint`"* routine — the same one track-load uses. **Once that call
  works, copy/paste, duplicate/array/mirror (8f), and cross-track save/insert are all the same
  primitive**, differing only in where the blueprints come from (memory vs. file) and how
  they're offset.
- **Design notes:** store each item's transform *relative to an anchor* (selection centroid or
  the gizmo) so paste lands relative to the cursor; assign **fresh `mo_groupId` GUIDs** on
  paste so a pasted group doesn't silently merge with the source group; for save/insert, reuse
  the game's `TrackBlueprint` XML serializer so stamps are forward-compatible with map files.
- **Suggested order:** (1) spike the spawn call on a *single* item; (2) single-item duplicate;
  (3) multi-object copy/paste; (4) save/insert to file. Each step is shippable on its own.
- **Files:** `PlacementUtilsWindow.cs` (selection → copy/paste/insert UI + hotkeys),
  `Shared.cs` (the clipboard), a new spawn helper (e.g. `Utils/ItemSpawner.cs`) wrapping the
  reflected game routine, `Utils/EditorUtils.cs`; a small serializer for the save-to-file
  format. Directly unblocks **8f**.

### 8a. Copy/paste MO config between objects  *(subset of the flagship above — ships first)*
- **Status: open.** **Effort: moderate** (pure C#).
- **What:** A clipboard in `Shared` that deep-copies the selected blueprint's
  `mo_animationOptions` + `mo_animationSteps` + `mo_triggerOptions`, with Copy/Paste buttons
  (or hotkeys) to stamp it onto another selection.
- **Why:** *The* fix for symmetric/repetitive tracks — author one moving gate, paste onto
  twenty.
- **Files:** `AnimationEditorWindow.cs`, `Shared.cs`.

### 8b. Editable / reorderable animation steps
- **Status: open.** **Effort: moderate** (mostly `AnimationEditorWindow.AddStepElement`).
- **What:** Today you can only delete a step, so a typo means deleting and re-capturing
  everything after it. Add per-row **Update-to-current** (re-capture from the gizmo),
  **move up/down**, and **insert**.
- **Why:** Turns step editing non-destructive.
- **Files:** `AnimationEditorWindow.cs`.

### 8c. Animation path preview
- **Status: open.** **Effort: moderate.**
- **What:** Draw a `LineRenderer` through the step positions (plus orientation ticks) in the
  editor so mappers see the trajectory without hitting Play. `Debug.DrawLine` won't show in
  the game view, so it needs a small overlay component.
- **Why:** High clarity-per-effort while authoring.
- **Files:** `AnimationEditorWindow.cs` (+ small overlay component).

### 8d. Timeline scrubber
- **Status: open.** **Effort: moderate.**
- **What:** A slider to scrub the animation instead of only Play/Stop — find the exact frame
  a platform blocks a gap.
- **Why:** Precise timing without repeatedly replaying.
- **Files:** `AnimationEditorWindow.cs` (drive the existing `AnimationPlayer` interpolation
  off a normalized time).

### 8e. Numeric transform entry + arrow-key nudge
- **Status: open.** **Effort: low-moderate.**
- **What:** Type exact position/rotation/scale for the selected gizmo, and nudge by the grid
  step with arrow keys.
- **Why:** Complements grid snapping for precise placement.
- **Files:** `PlacementUtilsWindow.cs`, `Utils/GridUtils.cs`.

### 8f. Duplicate / array / mirror placement
- **Status: open.** **Effort: moderate-high.**
- **What:** Clone the selected item *with* its MO config, offset it, or array N copies /
  mirror across an axis.
- **Why:** Fast construction of repetitive/symmetric track sections.
- **Depends on:** the same item-spawn spike as the **Multi-object copy/paste** flagship above
  — this is that primitive plus an offset/mirror pattern. Do the flagship spike first; then
  this is mostly UI.
- **Files:** `PlacementUtilsWindow.cs` + the shared spawn helper from the flagship.

### 8g. Trigger/portal validation (lint)
- **Status: open.** **Effort: low-moderate.**
- **What:** Warn in the window about dangling trigger targets (a `Target` with no matching
  `Name` marker), duplicate names, or teleport-with-no-target.
- **Why:** Catches the exact mistakes that silently break a map at flight time.
- **Files:** `AnimationEditorWindow.cs` / `PlacementUtilsWindow.cs`, `Utils/EditorUtils.cs`.

### 8h. Re-enable stats efficiently  *(FINISH — half-built)*
- **Status: partial.** **Effort: low.**
- **What:** The object/triangle counters were disabled because they re-scanned every mesh per
  second (a known stutter; labels are now pinned to `0`). Add an on-demand "Refresh stats"
  button instead of polling.
- **Why:** Brings back useful numbers without the per-second cost.
- **Files:** `PlacementUtilsWindow.cs`.

### Trigger-link gizmos  *(NEW)*
- **Status: open.** **Effort: moderate.**
- **What:** Draw a line from each entrance to its named exit marker plus an arrow for the exit
  facing, so portal links are visible in-editor (today you must fly the map to see where a
  portal sends you).
- **Why:** Makes portal authoring legible; pairs naturally with 8c (overlay rendering) and
  8g (validation).
- **Files:** `AnimationEditorWindow.cs` / `PlacementUtilsWindow.cs` (+ overlay component).

---

## Build / infrastructure / known issues

### Re-enable workshop preview override  *(FINISH — half-built)*
- **Status: partial.** **Effort: unknown** (blocked on a type reference).
- **What:** The `PopupShareContent.ShareItem` patch is disabled (`Plugin.cs:99-103`). The
  game's `ShareItem` gained a third parameter of an obfuscated type, so the original 2-arg
  Harmony patch couldn't bind, and HarmonyX throwing on a missing target previously aborted
  *all* of the mod's patches. Restore it once the new third-parameter type is referenceable.
- **Why:** Restores custom workshop preview images for shared maps.
- **Files:** `Plugin.cs`, `Patcher.cs` (if the type needs exposing at compile time).

### Group physics
- See **Group physics** under [Physics](#physics) — the biggest cross-cutting "finish-it"
  item; `FakeGroup` infra exists, the physics path is the missing piece.
