# World Navigation: What Was Built

The infrastructure for world navigation, ported from the WOTR accessibility mod and adapted to
DiscoAccess's architecture. This is the build record and decision log; the feasibility findings that
preceded it are in `world-navigation-scouting.md`. The leaf features that sit on this foundation
(sonar, wall tones, the scanner) are not built yet (see Deferred, below).

Everything here was validated live in Martinaise via the dev server, and the implementation was audited
line-by-line against the WOTR source; the divergences recorded below are deliberate.

## What it does today

A blind player can move a freeform cursor around the isometric scene with the live world keys and hear
its position relative to the character ("northeast, 2 meters"), with the cursor clamped to walkable
ground so it cannot leave the floor; press Enter to walk the character to the thing under the cursor and
interact (a conversation, a container, an exit) or to bare ground; and reach the game's information
screens, the pause and help menus, the status readouts (time, money, health), and the gameplay
quick-actions through the world keymap. The navigation model below is wired: being in the world owns the
keyboard the same way a migrated menu does. Underneath, a live registry classifies every entity in the
area and filters it down to the actionable set, and an audio engine can place stereo cues. What is still
missing to make the world fully legible is the leaf sensing systems (the sonar, wall tones, the scanner)
that turn the registry and the audio engine into a sense of what surrounds the cursor.

## Architecture

The world layer follows the project's existing four-project split, with the engine-coupled side divided
into a permanent host and a reloadable module.

- `DiscoAccess.Core` (no Unity reference) holds the engine-agnostic logic: the spatial math, the
  overlay framework, the taxonomy, the audio-engine contract, and the world-model contracts. Because
  Core cannot reference Unity, the world point type is `System.Numerics.Vector3` (XZ ground plane, Y up,
  one unit equals one meter), and the Module converts from Unity's `Vector3` at the boundary. This is
  what keeps the sensing logic unit-testable off-engine.
- `DiscoAccess.Module` (reloadable) holds the engine-touching adapters: the environment seam, the world
  registry and its proxies, and the world reader that drives the overlay. This is where the live game
  reads happen.
- `DiscoAccess` (permanent host) holds the NAudio engine, because the audio device is a native handle
  and must live beside Prism where it never reloads. It is lent to the module through `IModHost.Audio`,
  the same way speech and settings are lent.

The dependency shape is two foundations (the overlay framework and the world-model data layer) with the
sensing systems as leaves hanging off them. The framework and the data layer are independent of each
other; both must exist before any sensing system does.

## The pieces, by layer

Core spatial foundation (`Core/World/`):

- `Geo` computes straight-line (3D) distance, the eight-point compass bearing (planar, and reported as
  "no bearing" when a thing is directly above or below), the above/below vertical sign, and
  here-detection. It returns raw values; turning them into words is the announce layer's job, so Geo
  stays free of the string table.
- `Spatial` holds the sonar and wall-tone placement formulas (pan, distance volume, the quadratic
  proximity curve, and the sweep-gap pacing), ported from WOTR with distances in meters.
- `ScanBounds` is the shape geometry: a point, a circle footprint, a doorway's disjoint segments, and a
  connected polyline, each able to report its nearest point to a reference (so distance and bearing
  measure to the near edge of a wide thing, not its center).
- `SpatialReadout` composes the spoken cursor line (bearing first when there is one, then meters, then
  above/below) from authored strings.
- `WorldMath` is the shared clamp helper (netstandard2.0 has no `Math.Clamp`).

Core overlay framework (`Core/World/Overlays/`):

- `OverlaySystem` is a pure sensing provider: it queries the world relative to the cursor and either
  contributes a spoken announcement or makes sound each frame. It never moves the cursor and never reads
  input. Its play mode (Off / WhenMoving / Continuous) is read live through a bound provider.
- `Overlay` is the one container: it holds systems (one per concrete type), glides the cursor (only
  while the player has control), tracks motion, and runs the announce pipeline that joins every system's
  contribution into one spoken line.
- `Cursor` is the freeform-glide point of attention, navmesh-clamped through the environment seam.
- `IWorldEnvironment` is the engine seam the framework reads (player position, control state, navmesh
  trace). `MotionTracker`, `OverlayContext`, `OverlayAnnouncement`, `PlayMode`, and `AnnouncementContext`
  round it out.
- `SpatialSystem` (under `Overlays/Systems/`) is the first concrete system: the cursor point readout.

Core audio contract (`Core/Audio/`):

- `IAudioEngine` and `IWallTones` are the engine seam in plain floats: a panned one-shot and the four
  directional wall-tone voices. Sensing systems compute pan and volume themselves and hand the placed
  sound here.

Core world-model contracts (`Core/World/`):

- `IWorldItem` is the sensing-facing view of a thing (name, position, bounds, category, IsAccessible,
  IsVisible, Interact), implemented by a Module proxy that reads live.
- `IWorldModel` is the registry contract: the live collection of items plus `Added`/`Removed` events, so a
  consumer can attach to a thing (for example a sonar voice) and follow it rather than re-scanning.
- `WorldTaxonomy` is the flat category set (npc, door, exit, container, orb, other).

Host audio (`DiscoAccess/Audio/`):

- `NAudioEngine` is one shared mixer feeding one output device. Cues are generated procedurally (a
  windowed sine one-shot with constant-power pan; four continuous oscillators at distinct pitches and
  fixed compass pan for wall tones), so no authored sound assets are needed yet. The device opens lazily
  and self-disables on failure, so a machine with no audio device never crashes the mod.

Module world layer (`Module/World/`):

- `WorldEnvironment` implements `IWorldEnvironment` over the live game: player position from
  `Party.Player.Main`'s transform, control state, and a navmesh-clamped trace via `NavMesh.Raycast` plus
  `SamplePosition`.
- `WorldModel` is the poll-and-diff registry over `BasicEntity.sceneEntitySet` and the active
  `SenseOrb`s, keeping one stable proxy per game object, throttled to about 10 Hz.
- `EntityProxy` and `OrbProxy` are the live `IWorldItem` proxies. Entities classify by the game's
  `Interactable` subclass tree via `TryCast`; orbs read `GetText` and `orbType`. `WorldConvert`
  centralizes the Unity to System.Numerics conversion.
- `WorldReader` owns the one overlay, engages it on entering the world and disengages on leaving, and
  ticks both the registry and the overlay each frame. It is wired into `UiModule`.

## Key decisions

The simplified overlay model. WOTR lets the user cycle between named overlay presets. We dropped that:
Disco has one implicit overlay whose systems are toggled from the mod settings menu. The cycling was a
preset layer on top of the per-system toggles, not the toggles themselves, so dropping it also removes
WOTR's per-overlay settings-inheritance machinery. The three-way play mode (Off / WhenMoving /
Continuous) stays in the model even though the menu may present a plain on/off, so "sonar only while
gliding" can be surfaced later without rework.

No movement-mode abstraction. WOTR supports a tile-stepper and a freeform glider on one cursor. Disco
keeps only the freeform glide, so the `MovementMode` layer is gone and the cursor owns the one glide
directly.

Movement intent counts even when blocked. Holding the cursor keys registers as moving even when the
cursor cannot advance (against a wall) and even without control, matching WOTR, so a WhenMoving system
stays smooth instead of stuttering. An audio system that must fall silent during a cutscene gates on
control itself rather than on this motion signal (a requirement noted for the wall-tone and sonar
systems when they are built).

Core uses `System.Numerics.Vector3`, not Unity's. This is forced by Core having no Unity reference, and
it matches the project's "no Unity types past the boundary" rule. It needed the `System.Numerics.Vectors`
package, which is not in the netstandard2.0 surface by default.

The spoken distance is straight-line 3D, not planar. The city has real elevation within a single map
(only the Whirling's floors are separate maps reached by scene transitions), so a thing up on a ledge
reads as genuinely farther rather than only being tagged "above". The compass bearing stays planar (a
compass has no vertical component, and the readout omits it entirely when a thing is directly above or
below), and the above/below tag gives the vertical direction. This diverges from WOTR, which used the
game's rules-distance metric (half-weighting vertical gaps) that Disco has no equivalent for.

The in-world gate is `ViewType.CLEAR`. This is the non-obvious one. `ScreenAdapter` maps `ViewType.LOBBY`
to the world screen name and lists `CLEAR` among "silenced transition states", which suggests gating on
LOBBY. Verified live, the opposite is true: free-roam gameplay reads `CLEAR` steadily with the full
entity set present, while `LOBBY` reads false in-world. The code-review pass flagged this as a bug
(reasoning from the static mapping without running the game); it was rejected with the live evidence,
and the gate comment records why so it is not re-flagged. `HasControl` (a character exists and no
conversation is active) gates the finer cutscene/dialogue case on top.

Audio is NAudio via NuGet, host-side, procedural for now. The scouting notes established we must compute
pan and volume ourselves and stay off the game's mixer, so the cues are not colored by its DSP. NAudio
is the WOTR-proven choice and runs on BepInEx's CoreCLR. Cues are generated tones rather than sampled
WAVs, so the backbone is self-contained; sonar can pick a per-category pitch now and move to sampled
audio when assets are authored.

A flat taxonomy. WOTR has a two-level category tree. Disco's smaller world gets a flat set
(npc/door/exit/container/orb/other); the "what does the sonar sonify" toggle maps onto these. The
taxonomy is the coupling that matters most, since the scanner, the sonar sounds, and the announcements
all key off the same category strings, so it was worth getting the shape right before any leaf is built.

`IsAccessible` is the actionability gate. The registry holds everything (about 420 entities in
Martinaise), and `IsAccessible` collapses it to the roughly 100 actionable things a sighted player with
this build could act on, filtering the container clutter (368 containers down to about 62). The cursor
sees the full set; the sonar and scanner will read the accessible set.

The registry polls at about 10 Hz, not every frame. The per-frame cost is dominated by
`FindObjectsOfType`, a full scene scan, and membership need not be frame-fresh: the proxies read their
own live state on demand, and the consumers act on the order of seconds. This was a code-review fix.

Proxies read live, never cache. Per the project's "never cache game state" rule, every proxy property
reads the live game object on access. The only thing held is the proxy identity (so the scanner can keep
a selection), keyed on the Unity object via the interop wrapper cache.

## The seams

The boundaries between Core and the engine are three interfaces, each implemented in the Module or host:

- `IWorldEnvironment` (Module) gives the overlay framework the player position, control state, and
  navmesh clamp.
- `IAudioEngine` / `IWallTones` (host) give the sensing systems a place to play already-spatialized
  sound.
- `IWorldItem` / `IWorldModel` (Module) give the sensing systems the live entity data.

This is what lets the sensing logic live in Core and be unit-tested with fakes, while the live reads stay
in the Module.

## Dev hooks

`WorldReader` exposes a set of `Dev*` methods (and a static `Active` reference) so the dev server can
drive and inspect the cursor, the audio engine, and the registry. With the keyboard model now wired,
these are introspection and headless-validation scaffolding (a backgrounded window receives no OS keys,
so the live keys cannot be exercised through the dev server), not a player feature.

## The navigation model

The keyboard-ownership and interaction model was the deferred fork above the infrastructure; it is now
built and wired, and validated live in the Whirling backyard (glide to Yard Cuno and Enter starts his
conversation; the walled-off Yard Woodpile reads unreachable; the screen, status, and quick-action keys
all fire through the real input registry). It lives in three Module pieces: `WorldReader` promoted to the
world's keyboard owner, `WalkInteract` (the Enter state machine), and `WorldCommands` (the game-acting
hotkeys: screens, pause/help, status reads, quick-actions). The cursor verbs and the held glide are
read in `UiModule` alongside the menu input, under a new `InputCategory.World` that is live only while
the world owns the keyboard.

### Keyboard ownership: the world is the largest migrated screen

Being in the isometric world is owning the keyboard, the same way a migrated menu screen owns it. There
is no toggle mode to enter or leave. Whenever `ViewsPagesBridge.Current` reads `CLEAR`, `WorldReader`
takes the same one lever `ScreenManager` uses for menus, `InControl.InputManager.Enabled = false`,
reasserted each frame, and restores it on leaving the world. That lever is all-or-nothing: it mutes
every game key at once (scouting confirmed targeted mutes do not take), so the model is to mute the game
wholesale and re-provide each key we want. Our own keys poll `UnityEngine.Input` directly, below
InControl, so they keep working while the game's are dead; our actions call the game's APIs directly
(`Character.SetDestination`, `entity.Interact`), never through input, the same pattern the menu navigator
uses for `NavigationManager.Select`. A blind player gets nothing from the live camera or click
underneath, so there is no vanilla play to preserve, which is what makes the wholesale mute the right
choice over fragile per-key blocking.

The cost of muting everything is that every game action we still want needs its own binding. That is the
keymap below.

### The keymap

Cursor and interaction:
- W A S D glide the cursor on the ground plane (W north, S south, A west, D east), navmesh-clamped.
- C recenters the cursor on the character.
- Enter walks the character to the cursor's target and interacts (the walk-then-interact verb below); on
  bare ground it just walks there.
- Space cancels the current walk and stops movement (the game's `StopMovement`), so a committed Enter is
  abortable mid-path.

Information screens, the game's own hotkey letter plus Ctrl: Ctrl+I inventory, Ctrl+C character sheet,
Ctrl+J journal, Ctrl+T thought cabinet. Each opens the game's own view by invoking its HUD menu button's
click, and our screen reader then drives it; Escape (the screen's Back) closes it. Escape opens the pause
menu and F1 opens help, both through the game's own `ViewController.ToggleView`. There is no map key: the
map has no standalone view (it is a sub-page inside the journal, reachable via Ctrl+J), so a dedicated key
would need fragile cross-frame sub-page navigation for no access the journal does not already give. The
mod settings menu stays on Ctrl+M (the planned F12 move was only to free Ctrl+M for the dropped map key).

Gameplay quick-actions with no screen of their own:
- Left and Right arrow use a healing charge on the Health and Morale bars (matching the controller dpad's
  left and right), via the game's healing pools; refused, with feedback, when there is no charge or the
  bar is already full, so a charge is never wasted.
- 1 and 2 use the left-hand and right-hand equipped items (`InventoryLuaFunctions.UseSubstanceInHand`); an
  empty hand reads as such.
- F5 and F8 quicksave and quickload through the game's persistence API (quickload refused when no
  quicksave exists).
- Ctrl+L cycles language, global, in the world and in menus (the game's bare-key binding was killed by
  type-ahead, so it is restored here under Ctrl), then speaks the new language's own name.

Status:
- T reads the time (the game's own day-and-hour string), M reads money, H reads the two bars Health and
  Morale, each current of maximum with its count of healing charges. Each press re-reads. Plain keys,
  distinct by modifier from Ctrl+T (thought cabinet). The bars are named by the game's Health/Morale terms,
  not the Endurance/Volition skills that set their maximums.

Reserved for the leaf systems:
- Tab and Shift+Tab cycle the scanner through interactables when that system lands.
- Up and Down arrow, and Q, are currently unassigned in the world.

Talk-to-Kim is intentionally dropped: you reach the same conversation by walking to Kim and interacting,
so it needs no dedicated key.

### Enter: walk-then-interact

`Interact()` does not walk the character. Confirmed live: calling it on a container 11 meters off
returned false and the character did not move; it interacts in place and refuses when out of range,
playing a fail animation. The walk-then-interact fusion lives in the game's click handler, not in
`Interact()`, so Enter orchestrates it itself, with every piece proven live:

1. Target the entity's `GetInteractionLocation(currentLocation)` stand-point, never its raw
   `transform.position`. Many entities sit off the navmesh, doors embedded in walls, props up on ledges,
   so the raw position is unreachable; the stand-point is the game's designated on-navmesh approach spot.
2. `SetDestination` to the stand-point with `MovementMode.AUTOMATIC` (see Movement speed), watching
   `movementStatus` for arrival.
3. `Interact(new Interactable.ClickEventData())` once `IsWithinInteractionRadius` is true.

Both ends are confirmed live. Out of range, `Interact` returns false and the character stays put (the
woodpile at 11 meters). On arrival at a reachable target's stand-point it returns true and the
interaction begins: walking Harry to Yard Cuno's stand-point, `Interact` returned true once inside the
radius and a real conversation started (`isConversationActive`). Conversations do not change the view,
`ViewsPagesBridge.Current` stays `CLEAR` while one runs, which is why the world layer gates on
`HasControl` (no active conversation) on top of the `CLEAR` view.

Orbs and physical objects share this one path. An orb's clickable (`OrbUiElement`) only activates once
the character is inside the `SenseOrb`'s `InteractionRadius`, but the `SenseOrb` is already in the
registry at its world position while out of range, so the cursor can navigate to it before it is
clickable. Confirmed live: `IsOrbiting` flips exactly at the `InteractionRadius` (false at 3.5 meters,
true at 3.0 for a radius-3 orb), and crossing the radius only makes the orb appear, it does not auto-fire
the conversation. The clickable `OrbUI` carries a second gate, the orb must be rendered (the camera on
it); a distance-only approach with the camera elsewhere leaves the orb in range (`IsOrbiting` true) but
its `OrbUI` still inactive. In normal play the two coincide because the camera follows the character,
which is why camera-follow (Deferred) is required for orb interaction, not only for streaming. On bare
ground with no target, Enter is just `SetDestination`.

### Reachability is the game's own oracle, not ours

Before committing the walk, Enter checks reachability with the game's
`CheckIfCanCreatePathToHavePath(currentLocation)`. Do not roll our own `NavMesh.CalculatePath` to the
entity's body: an NPC's feet can sit on an off-mesh sliver, which produces false negatives (this falsely
reported the talkable Cunoesse as unreachable during testing). The game oracle tests the interaction
stand-point, which is what actually determines whether you can act, and it matches what is interactable
in real play. If it returns false, Enter announces the target cannot be reached from here rather than
walking partway and failing silently.

`IsAccessible` is not a reachability guarantee. The Yard Woodpile reads `IsAccessible = true` yet is
walled off from the backyard navmesh; its stand-point is on a separate navmesh component
(`CheckIfCanCreatePathToHavePath` false, confirmed by the game's own check and three independent navmesh
measurements). So reachability is always a live per-position check, never cached and never inferred from
`IsAccessible`; a thing unreachable from here can become reachable once the character has moved.

### Talk-across-barrier, and position versus actionability

The stand-point sits on the player's side of a barrier, so a fenced NPC is talkable without being
reachable, and Enter handles it with no special-casing. Cunoesse's body is 5.6 meters away behind a
fence (a `PathPartial`, unreachable), but her interaction stand-point is 3.9 meters on Harry's side (a
`PathComplete`); you walk to the stand-point and converse across the fence, exactly as a sighted player
does. So an entity's spoken position and its actionability are two independent facts: position describes
where the body is, for the player's spatial map; actionability is the oracle's verdict on the
stand-point. They can disagree, Cunoesse reads far and behind a fence yet is fully actionable, the
woodpile reads closer yet is not.

### The interaction point is what the player-facing tools represent

Every actionable interactable is represented to the player by its interaction point
(`GetInteractionLocation`), not its body: the sonar pings it, the scanner targets it, and the spoken
go-here distance and bearing measure to it. That is the point the player navigates to in order to act,
it sits on reachable ground, and it dissolves the barrier case (Cunoesse pings at her reachable
talk-spot, not her unreachable body), so following any cue leads to a successful interaction. The point
is approach-relative, recomputed from the querying position, so it is a live navigation target rather
than a fixed landmark; in practice it holds still as you walk straight at a thing and only shifts if a
better approach side opens. Computing it is heavier than reading a transform, so it is done for the
sonar's current sweep set and on scanner navigation, throttled, not for the whole accessible set every
frame.

The body position is kept for two narrow uses: the free-glide exploration cursor's spatial readout (what
is physically around you, the look-around sense), and locating a genuinely-unreachable thing to announce
it as behind a wall. So the proxy exposes three facts per item: `Position` (the body), `InteractionPoint`,
and `IsActionable` (the oracle's verdict). The actionable tools key off the interaction point; the
exploration cursor keys off the body.

### Movement speed

`SetDestination` takes a `MovementMode` (`AUTOMATIC`, `RUN`, `WALK`, `INSTANT`, `TELEPORT`). Enter passes
`AUTOMATIC`, which behaves identically to a vanilla click: the game's own policy decides walk versus run,
honoring the player's run preference and any scripted spot where the game wants careful movement. We
never hardcode `RUN`. There is no stamina cost to running, but some scripted moments reward moving
slowly, so forcing run would both break parity and risk overriding those; `AUTOMATIC` keeps us safe by
deferring to the game. An always-run preference, if wanted, is exposed through the game's own setting
mirrored in the mod menu, not forced per path, so it too stays subject to the game's contextual
overrides.

### What this changed in the code

`WorldReader` was promoted from a dev-driven cursor host to a real keyboard owner: `ResolveOwnership`
runs each frame before input is polled and, while the view is `CLEAR` with control and no menu screen
taking it, takes the InControl lever (yielding to the screen manager, which is authoritative), and the
held glide vector and the cursor verbs are read in `UiModule` under `InputCategory.World`, live only
while the world owns the keyboard. The lever is restored on leaving, never fighting the screen manager's.
The `Dev*` hooks remain only as introspection. The proxies (`EntityProxy`, `OrbProxy`) gained
`InteractionPoint(from)` and `IsActionable(from)`, both approach-relative and reading live through
`GetInteractionLocation` and `CheckIfCanCreatePathToHavePath` (an orb has no stand-point, so it reports
its body and is not actionable). The Enter verb is `WalkInteract`, a small arrival-watching state machine
(target the stand-point, drive `SetDestination` with `AUTOMATIC`, watch `movementStatus`, `Interact` on
arrival), cancellable by Space. The game-acting hotkeys (screens, pause/help, status reads,
quick-actions) live in `WorldCommands`, each calling the game's own method directly since the wholesale
mute leaves no key for the game to read.

## Deferred

These are real forks left open on purpose, not oversights.

- The held-glide and physical-keypress path is exercised in the design's mechanism but not through real
  OS keys in the headless dev harness: a backgrounded window receives no OS key events, and the dev
  server drives the menu navigator, not the world's raw-key poll. The cursor verbs and every game-acting
  handler were validated by firing their registered `InputAction` directly through the live `InputManager`
  (and the glide through the overlay), which exercises the same code a key would; the one unverified link
  is `Input.GetKey`/`GetKeyDown` itself returning for a held W or a pressed Enter, which uses the same
  poll the working menu keys do. Worth a sighted confirmation pass on a focused window.
- The dedicated map key. The map has no standalone view (it is a journal sub-page), so a Ctrl+M map key
  would need cross-frame sub-page navigation; dropped, since the map is reachable through Ctrl+J's journal.
- The leaf sensing systems: the sonar sweep, wall tones as a real system, and the scanner / review
  cursor. The infrastructure is shaped for them; they are the next features. The sonar must suppress
  out-of-sight things (those behind a wall from the cursor), or it is annoying; the gate is a navmesh
  line-of-sight raycast from the cursor to the thing's nearest point, added to the environment seam when
  the sonar is built. When the wall-tone system lands it must mute on loss of control (zero the volumes,
  keep the voices) rather than going silent abruptly.
- The settings-menu wiring for the world systems (the per-system on/off and "what does the sonar
  sonify" category toggles).
- Sampled audio assets, if the procedural cues prove insufficient.
- Bounds refinement: doorway segments and footprint circles. Every proxy currently reports a point
  bound; the richer shapes exist in `ScanBounds` but are not yet wired to the proxies.
- Name cleaning and the spoiler-safe fallback for slug names, per the naming rules in the scouting notes.
- Camera follow, so orbs stream in around the cursor as it explores. This is also required for orb
  interaction, not only reveal: an orbital orb's clickable `OrbUI` activates only when the orb is
  rendered, so the camera must be on a target before its orb can be interacted with (confirmed live, the
  in-range `IsOrbiting` flag flips at the radius but `OrbUI` stays inactive without the camera).
- Recentering the cursor onto the player when an area is entered.

## Validation snapshot

Loaded into the Whirling-in-Rags backyard: the registry classified about 420 entities (368 containers
matching the scouting probe, 9 NPCs, 6 exits, about 21 orbs), and `IsAccessible` filtered them to about
104 actionable. The cursor glided navmesh-clamped (stopping at a fence about 4 meters out, not running
to infinity) and recenter snapped it back to the character. Readouts spoke correct bearings and
distances. The audio device opened and played panned one-shots and wall tones with no error. 184 unit
tests pass.
