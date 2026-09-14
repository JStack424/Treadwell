# Changelog

## 0.1.1

Local test candidate.

- Added `Paved roads without stonecutter`, enabled by default, so the vanilla paved-road terrain piece can be placed outside stonecutter range.
- Replaced the unsuccessful requirement-check bypass with a direct, local recipe edit: the exact Paved Road piece's crafting-station reference is removed before its piece table refreshes.
- Preserved and safely restored the original stonecutter reference when the option is disabled, the scene unloads, or Treadwell shuts down.
- Kept the normal stone cost, unlock knowledge, placement checks, hoe stamina and durability, effects, repairs, and every unrelated piece or crafting station unchanged.
- Added regression coverage for exact recipe mutation, restoration, piece-table reloads, unrelated pieces, runtime conflicts, and the pinned lifecycle/field contract.

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
