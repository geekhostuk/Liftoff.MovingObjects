# Changelog

All notable changes to the **Liftoff MovingObjects** mod (the
[geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects)) are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Track files stay
backward-compatible: every option is off / zero by default, so existing maps are unaffected by an
upgrade.

> **Compatibility note.** From v1.3.7 onward, saved tracks are stamped with the minimum mod version
> needed to play them correctly (`mo_minModVersion`). An out-of-date mod leaves those objects static
> and logs "update your mod" rather than mis-playing. The stamped floor is bumped only for releases
> that change how a track *plays* — routine bugfix/editor releases don't raise it. See
> [`CONTRIBUTING.md`](CONTRIBUTING.md) for the mechanism.

## [Unreleased]

Planned and proposed work lives in [`ideas.md`](ideas.md) — a backlog grouped by system, each entry
tagged with a status and effort estimate.

## [1.3.9] - 2026-07-18

### Fixed
- **Editor frame drops on large maps (v1.3.8 regression).** The new undo/redo system resolved every
  tracked object by scanning the whole scene, and it did so once per object on every edit — so on a
  large map each move, nudge, or delete triggered a burst of full-scene searches and the editor
  hitched badly. Object resolution is now cached, so routine edits no longer scan the scene and the
  editor stays smooth regardless of how many objects the map contains. No change to what undo/redo
  does — only how fast it does it.

### Changed
- **Multiplayer spectator sync is now on by default.** Re-syncing a spectated pilot's moving objects
  used to be an opt-in experimental setting; it's now enabled out of the box, since it's the correct
  behaviour when spectating. It remains a toggle — the config key moved to `[Multiplayer]
  SpectatorAnimationSync` (default `true`) — so it can still be turned off if a session misbehaves.
  It stays best-effort: latency can cause brief clipping, and objects only re-align on a reset, so
  starting to spectate mid-lap stays out of step until the pilot next resets.
- **"Randomize phase" is now the same scatter on every PC.** The random start delay was drawn from
  a random number generator, so each player's game rolled its own — a field of objects looked
  scattered, but *differently* scattered for everyone, and a spectator could never match the pilot.
  The delay is now derived from the object's own identity in the track file, so every PC computes the
  same value: still scattered, but the same scatter everywhere. Two visible differences, both
  intended: the arrangement no longer re-rolls on each reset (it was previously a fresh roll every
  time you reset), and a given object always gets the same delay. Existing tracks keep working and
  need no re-save — the arrangement they show is simply a different (and now stable) one.
- **Spectator sync now re-syncs on the right event.** The sync previously watched the game log for the
  spectator camera attaching to a pilot. That line is real, but it fires when the camera *attaches* —
  i.e. when you switch who you're watching — **not** when the pilot you're watching resets. Measured
  over one live session: 6 of those camera attaches against 11 actual resets, including a run of 8
  consecutive resets it ignored. The sync now keys off the game's own multiplayer reset event instead,
  so every reset by the pilot you're watching re-aligns the moving objects, and only that pilot's
  resets do.

## [1.3.8] - 2026-07-13

### Added
- **Undo / Redo in the track builder.** **Ctrl+Z** undoes and **Ctrl+Y** redoes, with matching
  **Undo** / **Redo** buttons in the Placement utils window. It's editor-wide — it covers moving an
  object (gizmo drag or the numeric transform fields), placing an item from the palette, deleting,
  and every mod bulk action (paste, duplicate, array, mirror, insert-stamp) and group/ungroup — and
  it works for the game's own native placement/delete/drag as well as the mod's tools, because both
  funnel through the same add/remove chokepoints. A bulk action (e.g. pasting a whole group) undoes
  as one step; a quick drag-then-nudge on the same object folds into one step too. History holds the
  last 50 changes and is cleared each time you enter the editor.

### Notes
- Editor-only, non-breaking change — flight time and the save format are untouched, so existing
  tracks are unaffected. Per-object animation/trigger *config* edits in the detail pane aren't
  undoable yet; that's a possible follow-up.

## [1.3.7] - 2026-07-10

Two additions, both non-breaking — existing tracks are unaffected.

### Added
- **Show-Text triggers can stay on screen as long as you want.** Liftoff's native Show-Text trigger
  flashes its message for a fixed ~1 second with no way to change it. Show-Text items now have a
  **"Text time (s), 0=default"** field: set any positive number of seconds and the mod renders the
  message itself for exactly that long (centred, drop-shadowed, resolution-scaled). Left at `0` you
  get the game's original ~1 s flash. If anything goes wrong at flight time the mod quietly falls
  back to the game's default display.
- **Forward-compatibility version gate.** Saving a track now stamps it with the minimum mod version
  needed to play it correctly. Flying that track on an *older* mod build leaves every moving object
  static (and logs "update your mod") rather than half-playing an animation the older build doesn't
  fully understand. Older/un-stamped tracks are always treated as compatible; this only protects
  builds from 1.3.7 onward. The stamped floor is bumped only for releases that actually change how a
  track plays, so routine bugfix/editor releases won't block anyone.

## [1.3.6] - 2026-07-08

### Removed
- **Alt + Arrow gizmo nudging removed** (added in 1.3.2, taken out at Honk's request). It proved
  fiddly — it had to freeze the fly-camera while Alt was held, which paused mouse-look and repeatedly
  interfered with typing in text fields. The arrow keys now do exactly what vanilla Liftoff does: fly
  the editor camera, nothing more. Nudge objects with the numeric transform fields instead. Arrow
  keys still stay inside a focused text field (the 1.3.5 fix is unaffected).

## [1.3.5] - 2026-07-08

### Fixed
- **Arrow keys stay inside a text field while typing (the real fix).** Editing a mod text field and
  pressing an arrow at the end of the value could kick focus out of the field and move your avatar.
  The cause is UI Toolkit's directional focus-navigation: an arrow the field can't use for the caret
  gets turned into a focus-move that exits the field, and losing focus drops the game's "typing,
  don't move" suppression so the key reaches the fly-camera. The mod now swallows that navigation
  while a text field is focused, so the arrows stay in the field (caret only) and the avatar doesn't
  move. (Supersedes the 1.3.3 attempt, which mis-blamed the hold-Alt nudge and didn't fix it.)

## [1.3.4] - 2026-07-08

### Fixed
- **Deleting with F9 no longer leaves a "lonely" gizmo behind.** After F9-deleting a selected item
  or group, the game still had it selected in gizmo-manipulation mode, so the transform gizmo was
  left floating with nothing attached. Delete now drops the editor back to Place mode afterwards, so
  the game deselects and clears the gizmo. Selecting another item re-enters gizmo mode as normal.

## [1.3.3] - 2026-07-08

Follow-up fix to the 1.3.2 hold-Alt nudge.

### Fixed
- **Arrow keys behave normally again while typing in a field.** In 1.3.2 the fly-camera freeze
  engaged whenever **Alt** was held — including while editing a text field. Freezing disables the
  avatar's controller, which is what vanilla Liftoff relies on to keep arrow keys inside a focused
  field, so with Alt held an arrow key could move the avatar and kick focus out of the field. The mod
  now does nothing with the arrows while any text field is focused — no nudge, no freeze — so typing
  is pure vanilla again. Nudging outside fields (**Alt + arrows**) is unchanged.

## [1.3.2] - 2026-07-08

Two more editor fixes from Honk's playtest.

### Changed
- **Arrow-key nudge no longer flies your avatar too.** The editor fly-camera also moves on the arrow
  keys, so nudging the gizmo with the arrows moved *you* at the same time. Nudge became a **hold-Alt**
  action: **plain arrows fly the camera as normal**, and **Alt + arrows** nudge the gizmo only.
  **Shift** still means vertical. (This whole nudge feature was later removed in 1.3.6.)

### Fixed
- **Group edits no longer leave a "ghost" highlight behind.** After removing pieces from a group
  (Shift+MMB), regrouping, duplicating and re-merging, a leftover highlight overlay could linger at
  old positions (purely visual — it cleared on reload). Selection teardown now also sweeps highlight
  clones/markers whose carrier object had been deactivated, which it previously skipped.

## [1.3.1] - 2026-07-08

Bugfix release on top of the stable 1.3.0 line — small editor fixes/quality-of-life tweaks from
Honk's playtest feedback. No changes to flight-time behaviour or the save format.

### Fixed
- **Arrow-key nudge no longer fires while you're typing.** The arrow keys were moving the selected
  object while editing a text field, so a value edit could teleport the object. The guard only
  watched one of the mod's two editor panels; arrows are now suppressed whenever any editor text
  field has focus.
- **Grouped objects show their pink highlight immediately.** Selecting a group only tinted the
  members you'd already moused over, because the magenta highlight is cloned from the game's hover
  overlay, which the game builds lazily on first hover. The mod now forces that overlay to be built
  up front, so the whole group lights up on selection (also makes **Select all** show magenta on
  freshly loaded blocks).
- **Scaling a grouped object no longer drags the rest of the group around.** Resizing a scalable
  block that belonged to a selected group made every *other* member slide toward or away from it.
  Group-follow is now rigid: moving or rotating the group anchor moves the whole group together, but
  scaling a member is a purely local change.

### Changed
- **Animation step buttons are on one row.** The per-step buttons (Delete / Update / ↑ / ↓ / +) used
  to stack vertically; they're now a single compact row per step.
- **Faster step-list scrolling.** The wheel step for the animation step list (and the editor panel)
  is now much larger.

## [1.3.0] - 2026-07-08

First **stable** release of the moving-objects feature line. It promotes the work that shipped across
the 1.2.x betas out of beta — everything is confirmed working end-to-end **in-game (flight) and in
the track builder**. No new features over 1.2.11.

### Notes
- **Leaderboards on modded tracks now count.** Lugus (Liftoff's developer) enabled leaderboard times
  for modded tracks through their backend. This is a Liftoff-side change, not part of the mod.

## [1.2.11] - 2026-07-07

### Fixed
- **Grouped chambers animate in flight again — even when only some pieces carry the motion.** A whole
  grouped chamber that spun (or ran a 90° step rotation) perfectly in the editor could stand frozen
  in flight. A flight group runs **one motion driver** — the elected "root" member gets the player
  and the rest ride along — but election picked the *first member with any MO config*, not the one
  actually carrying motion, so a motionless piece could win and the group never moved. Election now
  prefers a piece that actually carries motion.

### Notes
- **Fully tested** — verified end-to-end both in-game (flight) and in the track builder. This
  release, and the paste/mirror/grouping/spinner work that landed across the 1.2.x betas, are
  confirmed working in a real session.
- **⚠️ Grouped motion caveat:** a flight group moves as **one body under one driver**. If a group
  contains *two different* motions — e.g. a spinner on one piece **and** a 90°-step door on another —
  only the elected root plays; the second motion is lost. Put independent motions in **separate
  groups** (or leave them ungrouped).

## [1.2.10] - 2026-07-06 (beta)

### Fixed
- **"Select all" now really catches everything.** In 1.2.9 it only picked up blocks the mouse cursor
  had already passed over, because the game builds each object's magenta hover-overlay lazily and
  select-all hung its selection marker on that overlay. Selection no longer depends on the overlay:
  every object gets a marker whether or not the game has built its overlay yet, so **Select all**
  captures the whole map on a freshly loaded track. (Cosmetic caveat: a never-hovered block is
  selected but may not *look* magenta until hovered — verify with F9/Copy/Ctrl+G.)

## [1.2.9] - 2026-07-06 (beta)

### Added
- **Select all objects.** New **"Select all objects"** button in the Placement utils window marks
  every placed item on the map as one flat multi-selection — so the whole track can be copied,
  mirrored, saved to a stamp, deleted (F9), or grabbed and moved in a single action. Existing groups
  are left untouched; **Ctrl+G** welds everything into one group if you want.

## [1.2.8] - 2026-07-06 (beta)

Group fixes from in-game playtest.

### Fixed
- **Grouped objects spin/move in flight again.** A group whose members all carried animation config
  froze in flight while spinning fine in preview: injection gave *every* member its own kinematic
  body and they fought over the shared pose to a standstill. Flight now runs **one motion driver per
  group** — only the group root gets a player, the other members are parented under it and ride
  along. (Also fixes a latent transform-cycle from the root being reparented under its own group
  object.)
- **Copies of copies are complete again.** Copy / duplicate (F5) / delete (F9) / mirror / stamp now
  take group membership from the authoritative `mo_groupId`, not from the pink-highlight markers. A
  freshly spawned copy hasn't got the game's selection overlay yet, so the marker-based capture
  silently dropped those members. Capture is now by group id, so every generation comes out whole.

### Changed
- **Animation editor panel fits the screen.** The panel content is now wrapped in a height-capped
  scroll view so the lower spinner/orbit/physics controls stay reachable on a 1080p screen.

## [1.2.7] - 2026-07-06 (beta)

Editor hotkeys and group editing.

### Added
- **F5 — Duplicate selection in place**, **F9 — Delete selection.** Hotkeys for the selection tools
  (both also have toolbar buttons). A pink group counts as one selection. Delete goes through the
  editor's own removal so it's gone from the saved track too.
- **Shift + middle-click edits group membership.** With a group (or any item) selected, Shift+MMB a
  loose object to **add** it, or Shift+MMB a current member to **remove** it. Shift+MMB from a lone
  selection seeds a new group.

### Documentation
- **Greatly expanded [User Guide](USERGUIDE.md)** with a large
  [Recipes cookbook](USERGUIDE.md#recipes--worked-examples) — worked examples with exact settings for
  fans, pendulums, elevators, portals, catapults, wind tunnels, speed pads, stamps, and more.

## [1.2.6] - 2026-07-06 (beta)

### Fixed
- **The origin bug is actually fixed this time.** Pasted / stamped / duplicated objects no longer
  collapse onto the origin after a save + reload. Root cause: a freshly spawned item is born with a
  default blueprint at the origin, and the mod registered *that* blueprint into the track before
  applying the real one. The mod now applies the blueprint *before* the item is registered, so the
  object that gets saved is the one you actually placed. (Tracks saved under the old bug keep their
  stray origin copies — delete those once.)

### Added
- **Copy/paste groups like stamp does.** Copying an ungrouped multi-selection now pastes back as one
  group, matching stamp behaviour.
- **"Duplicate selection in place" button.** Duplicates the current selection (a pink group counts as
  one) exactly on top of the originals as a fresh group — grab it and drag it off.

### Changed
- **A highlighted group is a real selection.** Clicking one object of a group (the whole group turns
  pink) now counts as selecting the whole group — Copy, Save-to-stamp, Mirror and Duplicate all
  capture every member instead of collapsing to just the clicked object.

## [1.2.5] - 2026-07-06 (beta)

Two more paste/stamp fixes from in-game playtest.

### Fixed
- **Pasted / stamped / duplicated objects stay where you put them after a save + reload.** Spawned
  items set their live position on screen but never wrote it into the blueprint the game serialises
  from, so they saved at `(0,0,0)` and reappeared stacked on the origin. Each spawned item now writes
  its final position/rotation into its own blueprint. (Tracks already saved with the old bug keep
  their stray origin copies.)

### Changed
- **Inserting a stamp always gives you one group.** Whether a stamp came in grouped used to depend on
  whether the original selection was grouped before you saved it. A stamp is now always inserted as a
  single cohesive group (ungroup with **G** to edit its pieces).

## [1.2.4] - 2026-07-05 (beta)

Follow-ups to the 1.2.3 copy/paste fix, from in-game playtest.

### Fixed
- **Pasted / mirrored objects keep their MO config and grouping.** Paste and mirror were writing the
  mod's config onto a throwaway blueprint that the game's spawn path ignores, so pasted objects lost
  their animation, trigger, and (most visibly) their **group**. The `mo_*` config is now written onto
  each spawned item's own blueprint, so a pasted group is a group again.
- **Paste lands on the grid.** A multi-object paste anchored its centroid on the gizmo, which for an
  even-count selection sits half a cell off-grid. The paste translation is now snapped to the grid
  step.

## [1.2.3] - 2026-07-05 (beta)

### Fixed
- **Multi-object copy/paste and mirror now keep their layout.** A pasted (or mirrored) selection used
  to collapse every object onto the cursor and lose its rotation, because the paste read each
  object's *blueprint* position/rotation — fields the game only syncs at save/load, so mid-edit
  they're stale. Copy/mirror now capture each item's **live** transform (position, rotation, scale)
  and set it explicitly on the spawned object. Save-to-stamp likewise records live positions.
- **Editor triangle stats no longer run on window open.** A later change had re-run the scene-wide
  triangle count every time the placement window opened. Stats are now computed **only** when you
  click *Refresh stats*.

## [1.2.2] - 2026-07-05 (beta)

### Fixed
- **Spinner / orbit now run in flight, not just in the editor.** Continuous-rotation and
  circular-orbit objects that had *no keyframe steps* previewed correctly but were never given a
  runtime player in flight — injection only fired for objects with at least one animation step.
  Spinner- or orbit-only objects now animate in flight.

### Added
- **Experimental spectator animation sync (opt-in, off by default).** When you spectate another pilot
  in multiplayer, your client never receives the local drone-reset events, so moving objects drift
  out of sync with what the spectated pilot sees. The new **`[Experimental] SpectatorAnimationSync`**
  config option watches the game log for the spectator camera re-attaching to a pilot and re-syncs on
  that pilot's reset. Best-effort and Liftoff-version-specific. Based on a proof-of-concept
  contributed by [AMPW-german](https://github.com/AMPW-german), reworked to run on the Unity main
  thread from a single non-threaded log subscription.

## [1.2.1] - 2026-07-04

### Fixed
- **Grouped-sphere physics fix** — a group of two half-spheres (or any grouped physics body) now
  collides and rolls as one solid object. Mesh colliders on a physics body are forced convex (Unity
  silently drops non-convex meshes from a dynamic rigidbody), and the **F2 physics preview** now
  builds a real compound body instead of just making the other members visually follow the root.

### Notes
- **Fully tested** — verified end-to-end both in-game (flight) and in the track builder.

## [1.2.0] - 2026-07-03

A large feature release implementing the remaining `ideas.md` backlog. Grouped by system.

### Added
- **Animation** — easing curves (SmoothStep / ease in/out), ping-pong / yo-yo playback, a spinner
  (constant-rotation) mode, an orbit / circular-path mode, and a per-object phase offset (with
  randomize) to desync fields of identical objects.
- **Physics** — an initial launch impulse/torque, and gravity-scale / drag / mass overrides (floaty,
  heavy, or anti-gravity bodies). **Grouped physics now works** (compound body on the group root).
- **Triggers & teleports** — one-shot / cooldown gates, sequential vs random exit markers, boost /
  brake gates (in-place speed rescale), wind / force volumes, speed-based routing, sound-on-trigger
  (drives the native sound item), and hazard-on-contact (kills the drone).
- **Editor authoring** — copy/paste of MO config between objects, editable / reorderable animation
  steps, an animation path preview, a timeline scrubber, numeric transform entry, trigger/portal
  validation (lint), on-demand object/triangle stats, and in-editor trigger-link gizmos.
- **Item spawning** — the mod can now instantiate track items from blueprints (the long-missing
  primitive), enabling single-item **Duplicate** and **Array** placement. Multi-object copy/paste,
  mirror, and save-to-file build on the same primitive.

### Fixed
- **Workshop preview override re-enabled.** `PopupShareContent.ShareItem` has a single overload, so
  it is now patched by name alone (no parameter types), which binds without naming the obfuscated
  third-parameter type; the `Sprite` preview is reached positionally. Sharing a track again honours a
  local `preview.png`.
- **Editor "Object count" / "Triangle count" no longer pinned to `0`.** Counting is restored behind
  an on-demand "Refresh stats" button (plus one count on window open) instead of the per-second poll
  that stuttered on large maps; triangle counts use `GetIndexCount` to avoid per-mesh allocations.

## [1.1.2] - 2026-07-02

### Added
- **Trigger action: (re)start vs. stop** — a `Restart`/`Stop` option on animations, so a trigger can
  now switch a running animation *off* (freeze in place), the mirror of the existing "start on
  trigger" behavior.

### Notes
- Verified against **Liftoff 1.7.4**.

## [1.1.1] - 2026-06-30

### Added
- **Portal-style teleports** — opt-in seamless teleports that carry the drone's momentum and
  orientation into the exit gate's frame, with an optional exit-speed override.
- **High-speed trigger reliability** — continuous-collision watchdog plus swept-ray detection so
  triggers no longer get tunnelled through by a fast drone.

## [1.1.0] - 2026-05-08

First release of the [geekhostuk fork](https://github.com/geekhostuk/Liftoff.MovingObjects). The
upstream 1.0.14 plugin still loaded against current Liftoff builds (Unity 2022.3, BepInEx 5.4.23) but
objects on modded maps no longer animated. v1.1.0 restores the runtime against the current game — see
[why upstream broke](#why-upstream-1014-stopped-working) below.

### What's verified working
- Object animation, physics, and triggers on community maps (e.g. *Honkey Kong*).
- Flying through the JMT-MOD multiplayer room.
- Track editor extensions (animation editor window, placement utilities) for authors.
- Animations correctly re-bind after switching tracks within a session.

---

## Why upstream 1.0.14 stopped working

The upstream release was last published in early 2024 and stopped working against current Liftoff
builds. Three independent issues, uncovered via runtime tracing, are what v1.1.0 fixed. They remain
recorded here because they're the hook points the mod depends on — `tools/PatchHelper audit` checks
they still exist after a game update (see [`CONTRIBUTING.md`](CONTRIBUTING.md)).

1. **`PopupShareContent.ShareItem` signature changed.** HarmonyX raised an exception out of
   `Harmony.CreateAndPatchAll`, aborting registration of every other patch in the plugin. The mod
   loaded, but no patches were actually attached.
2. **The plugin's `OnDestroy` called `Harmony.UnpatchSelf()`.** The current game destroys the BepInEx
   plugin GameObject during the bootstrap-scene unload, very early in startup — so once the rest of
   the patches *did* attach, this teardown promptly removed them again before any flight session.
3. **Game refactor: `LevelInitSequence.InitializeLevel` and `FlightManager.ResetDroneRoutine` are now
   dead code.** The methods still exist (so Harmony resolves them silently) but are never called by
   the current game flow. Animation/physics injection now hooks `FlightManager.Start` and subscribes
   to the parameterless `onDroneResetStart` / `onDroneResetDone` events instead.
