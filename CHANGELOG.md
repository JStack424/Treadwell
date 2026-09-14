# Changelog

## 0.1.1

Local test candidate.

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
