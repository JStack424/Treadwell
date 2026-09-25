# Changelog

## 0.2.2

- Corrected Pathen's placement indicator after inspecting the exact Valheim 1.0.14 `path_v2` prefab: its direct `_GhostOnly` child is serialized inactive, rotated 90 degrees, and uses a 4×4×1 local scale, while the visible nested particle uses Local scaling and therefore ignores its parent's scale. Treadwell now scales the marker plus each nested Local-mode particle's own transform from its captured baseline, without double-scaling particles attached directly to the marker.
- Addressed the actual-effect failure shared by Level Ground and Pathen after tracing `Player.PlacePiece` → cloned `TerrainOp.Awake` → `TerrainComp.ApplyOperation` → `TerrainOp.Settings.Deserialize`: Valheim uses the selected prefab to size the initial heightmap search but serializes only the operation identity, then resolves the applied settings again from `ObjectDB`. Treadwell now mutates both exact settings sources before cloning, keeps them changed through the synchronous local RPC, and restores both in the finalizer, deduplicating shared references.
- Retained a hash-pinned extraction record for Pathen's direct inactive, rotated 4×4×1 marker and added executable coverage for the actual dual-source mutation session: Level Ground and Pathen clone/effect lifecycle, the 0.2.1 selected-only failure shape, distinct and shared settings, disabled channels, conflict-safe restoration, partial-application rollback, uniform marker scaling, and pinned call order through the terrain-RPC/ObjectDB resolution path.
- Preserved the confirmed Alt-wheel camera-zoom suppression, Level Ground's 1–8 m range, Pathen/Paved Road's 1–10 m ranges, Paved Road's 2.2:3 paint-to-smooth ratio, Raise Ground exclusion, per-action session-only radii, and every 0.1.2 road feature.
- This remains a local test candidate. Level Ground's actual-effect growth, Pathen's visible-indicator and actual-effect growth, and remote-owner/multiplayer radius propagation remain explicitly live-unverified.

## 0.2.1

- Prevented camera zoom while Alt+scroll is routed through the vanilla hoe placement mode, including radius bounds, rejected/no-op adjustments, and ineligible hoe actions; zoom without Alt, with another tool, or in the build-selection UI remains vanilla.
- Scoped wheel suppression to the exact `GameCamera.UpdateCamera(float)` call and restored it in a Harmony finalizer, leaving Treadwell's placement input, build-menu selection, rotation, controllers, and unrelated tools untouched.
- Reduced Level Ground's maximum radius from 10 m to 8 m; Pathen and Paved Road remain independently adjustable from 1–10 m in 0.5 m steps.
- Corrected Pathen recognition by treating runtime prefab object names as diagnostics while retaining exact localized identity, complete three-brush uniqueness, operation/recipe fingerprints, root-component constraints, and vanilla 3/2/3 m baselines.
- Kept Pathen's independent 2 m session baseline and synchronized proportional indicator/effect scaling.
- Added exact runtime and pinned-IL contracts for `GameCamera.UpdateCamera(float)` and its two `ZInput.GetMouseScrollWheel()` reads, plus regression coverage for camera-routing noninterference, per-action bounds, and Pathen live-object naming.
- Preserved all 0.1.2 road bonuses, stonecutter-free Paved Road behavior, transactional terrain mutation/restoration, and Raise Ground exclusion.
- This remains a live-unverified test candidate. Remote-owner and multiplayer radius propagation remains explicitly unverified.

## 0.2.0

- Added session-only terrain-radius controls for the hoe's exact vanilla Level Ground, Pathen, and Paved Road actions: hold Left Alt or Right Alt and scroll in 0.5 m steps from 1–10 m.
- Gave each action its own session-only vanilla baseline (Level Ground 3 m, Pathen 2 m, Paved Road 3 m) and kept the visible/effect scale proportional to that action.
- Kept the placement brush and real terrain operation in proportional lockstep by uniformly scaling the placement ghost's active `_GhostOnly` geometry and proportionally scaling every enabled `TerrainOp.Settings` radius. Paved Road retains its vanilla 2.2:3 paint-to-smooth ratio; disabled channels remain untouched.
- Limited recognition to one unique exact vanilla three-piece set in the active hoe table, with exact prefab/piece identities, one root Piece, one root TerrainOp, no legacy TerrainModifier, no rotation, verified operation/recipe shapes, and exact 3/2/3 m baselines. Raise Ground, cultivator actions, removal mode, hammer pieces, other actions, changed pieces, and ambiguous/modded duplicates fail closed.
- Consumed mouse-wheel input only while Alt is held with an eligible synchronized brush; ordinary scroll and rotation behavior remains vanilla everywhere else.
- Applied terrain-radius fields only around Valheim's synchronous `Player.PlacePiece` clone, with finalizer restoration, conditional conflict-safe cleanup, and reset paths for selection/tool changes, scene unload, disable/unload, and exceptions.
- Expanded runtime contracts and pinned 1.0.14 assembly checks for placement input, selection, placement cloning, terrain-radius fields, visible marker scaling, and mouse-wheel suppression.
- Preserved all live-successful 0.1.2 road bonuses, compatibility behavior, and Paved Road stonecutter removal.
- This is a test build pending live validation of radius input and brush/effect lockstep. Remote-owner and multiplayer behavior is explicitly unverified because Valheim's terrain RPC serializes the operation prefab identity, not custom radius values.

## 0.1.2

- Replaced exact runtime game-version, assembly SHA-256, and MVID blocking with a contract-based compatibility gate, so compatible client/server builds and minor Valheim patches are not disabled solely because their build identity differs.
- Kept pinned assembly hashes and MVIDs as build/reference provenance checks only.
- Added exact, unique runtime checks for every patched target, Harmony constructor/API and patch method, accessed game member, and Unity road-discovery contract; missing or ambiguous signatures still fail closed before Harmony construction or gameplay-hook installation.
- Strengthened transactional patch rollback so every cleanup step runs, gameplay entrypoints are cleared, the owned Paved Road station is restored, and incomplete restoration state is retained for a safe cleanup retry before Treadwell remains disabled.
- Preserved all paved-road placement, movement, configuration, multiplayer, and client-only behavior unchanged.

## 0.1.1

Release package prepared with the exact DLL bytes supplied for live validation.

- Revalidated unchanged 0.1.1 gameplay and configuration against Valheim 1.0.14, anonymous Steam dedicated-server build 25364309, and Unity 6000.0.75f1.
- Updated the fail-closed runtime fingerprint, private reference bundle, metadata/IL contracts, documentation, and deterministic test package.
- Added `Paved roads without stonecutter`, enabled by default, so the vanilla paved-road terrain piece can be placed outside stonecutter range.
- Replaced the unsuccessful requirement-check bypass with a direct, local recipe edit: the exact Paved Road piece's crafting-station reference is removed as Valheim enters place mode, before its piece table refreshes, and immediately before vanilla requirement checks.
- Fixed a second test-build failure caused by incorrectly requiring the live station object to carry exact stonecutter prefab and display identifiers; Treadwell now captures and clears whatever non-null station reference is attached to the exact Paved Road piece.
- Replaced brittle `paved_road` / `$piece_pavedroad` identity gates after live diagnostics proved the active entry did not match both guessed names. Discovery now uses the pinned root-Piece table structure plus one paved terrain operation, a station requirement, and one single-unit Stone resource requirement; Paved Road and station names are diagnostic only; the expected Stone resource prefab remains part of the semantic constraint.
- Traverses child components for terrain operations, requires exactly one semantic candidate, and fails closed on zero or multiple matches.
- Preserved and safely restored the captured station reference when the option is disabled, the scene unloads, or Treadwell shuts down.
- Added one-time bounded BepInEx diagnostics for successful semantic selection and for zero/ambiguous matches, including sanitized candidate shapes and names without adding in-game messages.
- Kept the normal stone cost, unlock knowledge, placement checks, hoe stamina and durability, effects, repairs, and every unrelated piece or crafting station unchanged.
- Added regression coverage for station/name independence, semantic discovery, zero/multiple-match rejection, nested terrain shapes, real place-mode and placement-check lifecycles, restoration, piece-table reloads, runtime conflicts, and setting changes.

## 0.1.0

Initial public release.

- Added configurable sprint bonuses for vanilla dirt paths: 10% more speed and 10% less sprint-stamina use by default.
- Added independently configurable paved-road bonuses: 20% more speed and 20% less sprint-stamina use by default.
- Constrained all four percentage settings to 0–100 and included a master enable switch.
- Preserved Valheim's Run skill, equipment, status-effect, and global movement calculations by applying multiplicative modifiers.
- Added short path-edge smoothing with immediate clearing on cultivated soil, floors, non-terrain surfaces, and leaving the ground.
- Limited all effects to the local player while sprinting; carts, creatures, other movement actions, UI, sounds, and world state are unchanged.
- Added exact fail-closed compatibility checks for Valheim 1.0.12 / Steam build 25253764.
- Live-tested the core road bonuses in Valheim.
- Added pure behavior tests, independent assembly-contract tests, pinned reference verification, deterministic packaging, and an exact five-file package audit.
