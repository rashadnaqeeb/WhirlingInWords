# World Navigation: What Was Built

The infrastructure for world navigation, ported from the WOTR accessibility mod and adapted to
DiscoAccess's architecture. This is the build record and decision log; the feasibility findings that
preceded it are in `world-navigation-scouting.md`. The leaf features that sit on this foundation
(sonar, wall tones, the scanner) are not built yet (see Deferred, below).

Everything here was validated live in Martinaise via the dev server, and the implementation was audited
line-by-line against the WOTR source; the divergences recorded below are deliberate.

## What it does today

A blind player can already, through the dev hooks, move a freeform cursor around the isometric scene
and hear its position relative to the character ("northeast, 2 meters"), with the cursor clamped to
walkable ground so it cannot leave the floor. Underneath, a live registry classifies every entity in
the area and filters it down to the actionable set, and an audio engine can place stereo cues. What is
missing to make this player-facing is the keyboard model (live cursor keys) and the sensing systems
that turn the registry and the audio engine into sonar and wall tones.

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
drive and inspect the cursor, the audio engine, and the registry while live keybindings are pending.
These are validation scaffolding, not a player feature; they will be pruned or replaced when the
keyboard model lands.

## Deferred

These are real forks left open on purpose, not oversights.

- The world keyboard-ownership / focus-mode model. How the player enters and exits world navigation, and
  how the cursor keys coexist with the game's own hotkeys, is a UX decision to make before wiring live
  keys. Until then the cursor is driven through the dev hooks.
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
- Camera follow, so orbs stream in around the cursor as it explores.
- Recentering the cursor onto the player when an area is entered.

## Validation snapshot

Loaded into the Whirling-in-Rags backyard: the registry classified about 420 entities (368 containers
matching the scouting probe, 9 NPCs, 6 exits, about 21 orbs), and `IsAccessible` filtered them to about
104 actionable. The cursor glided navmesh-clamped (stopping at a fence about 4 meters out, not running
to infinity) and recenter snapped it back to the character. Readouts spoke correct bearings and
distances. The audio device opened and played panned one-shots and wall tones with no error. 184 unit
tests pass.
