# Changelog

## 0.1.0

- Added configurable sprint-speed and sprint-stamina bonuses for vanilla dirt paths and paved roads, defaulting to 10% on dirt and 20% on paved roads.
- Preserved Valheim's Run skill, equipment, status-effect, and global movement calculations by applying multiplicative modifiers.
- Added short path-edge smoothing with immediate clearing on cultivated soil, floors, non-terrain surfaces, and leaving the ground.
- Limited all effects to the local player while sprinting; no carts, creatures, other movement actions, UI, sounds, or world state are changed.
- Added exact fail-closed compatibility checks for Valheim 1.0.12 / Steam build 25253764.
- Added pure behavior tests, independent assembly-contract tests, deterministic packaging, and a package-content audit.
