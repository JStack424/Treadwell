# Changelog

## 0.1.1

Local test candidate.

- Added `Paved roads without stonecutter`, enabled by default, so the vanilla paved-road terrain piece can be placed outside stonecutter range.
- Kept the normal stone cost, unlock knowledge, placement checks, hoe stamina and durability, effects, repairs, and every unrelated piece or crafting station unchanged.
- Disabling the option restores Valheim's exact nearby-stonecutter requirement.
- Fixed the live placement failure by removing an invalid dependency on the runtime `TerrainModifier` component location from the build-menu requirement check; exact identification now uses the vanilla piece and station prefab/display identities.
- Added regression coverage for the component-layout-independent policy and pinned UI, HUD, placement, station-failure, and `TryPlacePiece` call paths.

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
