# Feature Ideas — Liftoff.MovingObjects

A running list of cool, low-to-moderate-effort features for the mod, grouped by effort.
Each entry notes what it does, why it's worth doing, and which files it touches.

Adding an **author-facing option** generally touches three layers:
1. A serialized field in `Liftoff.MovingObjects.Patcher/Patcher.cs` (so it round-trips in `TrackBlueprint`).
2. The editor UI — either the compiled `liftoffui.bundle` (needs Unity to rebuild) or added
   in code in `AnimationEditorWindow.cs` (preferred when the bundle can't be rebuilt).
3. The runtime reader (`TriggerBehavior.cs`, `Player/AnimationPlayer.cs`, or `Player/PhysicsPlayer.cs`).

Features that reuse existing data or are pure runtime changes are the cheapest.

---

## Implemented

### ✅ 1. Portal-style (seamless) teleport
**Status: done.** Opt-in `seamlessTeleport` flag plus an `exitSpeed` (km/h) override on trigger
teleports.

- **What it does:** Instead of just moving the drone and keeping its world-space velocity (which
  makes you exit flying sideways), a seamless teleport re-expresses the drone's entry velocity and
  orientation in the destination's frame. Fly straight through → exit straight along the exit gate's
  forward; enter at an angle → exit deflected by the same angle, carrying momentum. Just like *Portal*.
- **Exit speed:** `0` preserves entry speed (true momentum); `> 0` overrides the magnitude while
  keeping the computed direction — turning a portal into a launch pad or brake gate.
- **Authoring:** Exit marker = object with trigger `Name`; rotate it to aim the exit. Entrance =
  checkpoint with trigger `Target` + `Teleport` + `Seamless teleport` (+ optional `Exit speed`).
- **Math:** `entryToExit = dst.rotation * inverse(src.rotation)`, applied to both velocity and
  rotation; `src` = entrance transform, `dst` = chosen destination marker transform.
- **Backward compatible:** defaults off, so existing maps are unaffected.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`, `Plugin.cs`.

---

## Top picks (high payoff, low effort)

### 2. Animation easing
- **What:** Steps currently interpolate with raw linear `Lerp` (`AnimationPlayer.cs`), which looks
  robotic. Add a `smooth` flag that swaps in an ease (e.g. `Mathf.SmoothStep` on `t`).
- **Why:** Pendulums, elevators, doors, and platforms instantly look far better. Roughly a one-line
  math change behind a bool option.
- **Effort:** Low. Patcher field + editor toggle + 1-line runtime change.
- **Files:** `Patcher.cs` (`MO_AnimationOptions`), `AnimationEditorWindow.cs`, `AnimationPlayer.cs`.

### 3. Ping-pong / yo-yo animations
- **What:** A `pingPong` option that plays steps forward then in reverse, instead of looping back to
  step 0.
- **Why:** Mappers author *half* an animation (e.g. a gate opening) and get the return for free.
  Great for oscillating hazards.
- **Effort:** Low-moderate. Reverse the cached steps on alternate passes in the animation coroutine.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `AnimationPlayer.cs`.

---

## Also easy, smaller wow

### 4. One-shot / cooldown triggers
- **What:** A `triggerOnce` bool (fires only the first pass per flight) and/or a re-arm cooldown
  (seconds before the trigger can fire again).
- **Why:** One-time events (a single drop, a one-shot teleport) and rate-limiting. We're already
  deep in this code path from the anti-tunneling work, so it's a small guard addition.
- **Effort:** Low. Patcher field(s) + editor control(s) + a guard/timer in `TriggerBehavior`.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`.

### 5. Initial launch impulse for physics objects
- **What:** Physics objects only drop under gravity today. Add a configurable launch velocity /
  direction applied when they go dynamic.
- **Why:** Catapults, swinging hazards, "popcorn" obstacles — far more dynamic than gravity drops.
- **Effort:** Moderate. Apply an initial velocity when `PhysicsPlayer` un-kinematics the body.
- **Files:** `Patcher.cs` (`MO_AnimationOptions`), `AnimationEditorWindow.cs`, `PhysicsPlayer.cs`.

### 6. Sequential vs random teleport targets
- **What:** When several exit markers share a name, teleport currently picks one at **random**. Add
  a `sequential` option to cycle through them in order.
- **Why:** Predictable multi-exit portals / routing. Random is already a feature (scatter portal);
  this just adds the deterministic alternative.
- **Effort:** Low. A counter instead of `Random.Range` in `TriggerBehavior`.
- **Files:** `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`.

---

## More work (flagged for honesty)

### 7. Trigger sound effects
- **What:** Play a sound on trigger / teleport / animation start.
- **Why:** Big immersion boost — portals and moving hazards feel real with audio cues.
- **Effort:** Moderate-high. Needs audio asset loading/bundling and an `AudioSource` at the trigger,
  plus a way for mappers to pick/reference a clip.
- **Files:** asset bundle / audio loading, `Patcher.cs`, `AnimationEditorWindow.cs`, `TriggerBehavior.cs`.

### 8. Editor QoL suite
Grouped below. **Current editor state for context:** grid snap (F1/button), drag snap, noclip (F3),
wireframe (F4), "enchanted" middle-click selection + Ctrl+G grouping, and the animation/physics/
trigger panels. Key gaps: animation **steps are display-only** (each row shows position/rotation as
labels with just time/delay + a delete button — no edit/reorder), stats (object/triangle count) are
disabled, and there's no copy/paste, path preview, or validation.

#### Authoring flow (biggest wins)
- **8a. Copy/paste MO config between objects.** A clipboard in `Shared` that deep-copies the selected
  blueprint's `mo_animationOptions` + `mo_animationSteps` + `mo_triggerOptions`, with Copy/Paste
  buttons (or hotkeys) to stamp it onto another selection. *The* fix for symmetric/repetitive tracks —
  author one moving gate, paste onto twenty. **Effort:** Moderate, pure C#.
  **Files:** `AnimationEditorWindow.cs`, `Shared.cs`.
- **8b. Editable / reorderable animation steps.** Today you can only delete a step, so a typo means
  deleting and re-capturing everything after it. Add per-row **Update-to-current** (re-capture from
  the gizmo), **move up/down**, and **insert**. Turns step editing non-destructive.
  **Effort:** Moderate; mostly `AnimationEditorWindow.AddStepElement`.

#### Animation-specific
- **8c. Animation path preview.** Draw a `LineRenderer` through the step positions (plus orientation
  ticks) in the editor so mappers see the trajectory without hitting Play. `Debug.DrawLine` won't show
  in the game view, so it needs a small overlay component. High clarity-per-effort. **Effort:** Moderate.
- **8d. Timeline scrubber.** A slider to scrub the animation instead of only Play/Stop — find the exact
  frame a platform blocks a gap. **Effort:** Moderate (drive the existing `AnimationPlayer`
  interpolation off a normalized time).

#### Placement
- **8e. Numeric transform entry + arrow-key nudge.** Type exact position/rotation/scale for the
  selected gizmo, and nudge by the grid step with arrow keys. Complements grid snapping for precise
  placement. **Effort:** Low–moderate. **Files:** `PlacementUtilsWindow.cs`, `GridUtils.cs`.
- **8f. Duplicate / array / mirror placement.** Clone the selected item *with* its MO config, offset it,
  or array N copies / mirror across an axis. Builds on 8a. Depends on the game's item-spawn API, so the
  most "more work" of these. **Effort:** Moderate–high.

#### Safety & feedback (cheap, prevents broken maps)
- **8g. Trigger/portal validation (lint).** Warn in the window about dangling trigger targets (a
  `Target` with no matching `Name` marker), duplicate names, or teleport-with-no-target. Catches the
  exact mistakes that silently break a map at flight time. **Effort:** Low–moderate.
- **8h. Re-enable stats efficiently.** The object/triangle counters were disabled because they
  re-scanned every mesh per second (a known stutter). An on-demand "Refresh stats" button instead of
  polling brings them back without the cost. **Effort:** Low. **Files:** `PlacementUtilsWindow.cs`.

#### Pairs well with the portal feature (#1)
- **Trigger gizmos:** extend 8c to draw a line from each entrance to its named exit marker plus an
  arrow for the exit facing, so portal links are visible in-editor (today you must fly the map to see
  where a portal sends you).
- **Validation (8g):** a portal entrance pointing at a non-existent exit name is an easy mistake and
  currently fails silently.

**Suggested starting point:** 8a (copy/paste) and 8b (editable steps) — they remove the most
repetitive pain and are self-contained C# with no asset-bundle rebuild needed.

---

## Related robustness note (already done)

The teleport/animation/physics triggers previously **missed fast passes** because Unity's
`OnTriggerEnter` only fires when the drone overlaps the collider on a physics step — a fast drone
tunnels straight through. Fixed by (a) a continuous-collision watchdog on the drone and (b) manual
**swept-ray detection** of the drone's path against trigger colliders in `TriggerBehavior.FixedUpdate`.
This guarantees triggers fire at any speed, which all the trigger-based ideas above depend on.
