# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus mod that makes Valheim's roads less tedious to build and more rewarding to use:

- **Paved-road building:** place the vanilla paved-road terrain piece without a nearby stonecutter by default, while keeping its stone cost and every other placement rule.
- **Dirt paths:** 10% faster sprinting and 10% less sprint-stamina use by default.
- **Paved roads:** 20% faster sprinting and 20% less sprint-stamina use by default.
- Each of the four bonuses is independently configurable from 0% to 100%.

Only the local player's sprinting on vanilla terrain-painted paths is changed. Walking, sneaking, swimming, jumping, dodging, attacks, carts, creatures, natural terrain, cultivated soil, and building floors are untouched. A short edge-smoothing window prevents bonuses from flickering across tiny gaps in road paint, while cultivated soil, floors, leaving the ground, and other non-terrain surfaces clear them immediately.

Treadwell has no status icon, popup, sound, gameplay message, custom world state, or server requirement.

## Installation

Install with r2modman or Thunderstore Mod Manager, or extract `plugins/Treadwell/Treadwell.dll` into your Valheim installation's `BepInEx/plugins/Treadwell/` directory.

[BepInExPack for Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) is required.

Each player who wants the building convenience or movement bonuses installs Treadwell on their own client. No server installation or configuration synchronization is required.

## Configuration

Treadwell creates six settings in `BepInEx/config/com.jstack424.treadwell.cfg`:

- Master enable
- Paved roads without stonecutter (default enabled)
- Dirt sprint-speed bonus percentage (default 10%)
- Dirt sprint-stamina reduction percentage (default 10%)
- Paved sprint-speed bonus percentage (default 20%)
- Paved sprint-stamina reduction percentage (default 20%)

The stonecutter option is read live. When enabled, Treadwell requires exactly one active-table entry with the semantic Paved Road shape: one root Piece with no additional child Pieces, one paved terrain operation (including child components), an attached crafting-station requirement, and one single-unit Stone resource requirement. The entry's root, Piece, station, and localized names are logged for diagnostics but are not identity gates; the expected Stone resource prefab is checked as part of the semantic shape. Treadwell removes only that Piece's station reference locally; disabling restores the exact captured object. Zero or multiple matches fail closed and leave vanilla requirements intact. Stone cost, unlock knowledge, hoe behavior, repairs, and unrelated pieces remain unchanged. All percentages are independently constrained to 0–100 and are read live.

If you used an earlier test build, BepInEx may retain its older values. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to 10, 10, 20, and 20.

## Compatibility and safety

Treadwell 0.1.2 uses a contract-based compatibility gate. Valheim's displayed version, Unity version, assembly hash, and MVID are not runtime allowlists, so compatible minor patches and client/server assembly variants are not rejected solely because their build identity differs. Before installing anything, Treadwell requires one exact match for every patched target and overload, matching Harmony patch methods, every accessed game field, and the Unity road-discovery and terrain contracts it needs. Missing, ambiguous, or changed contracts still disable Treadwell safely. A partial patch installation is rolled back before the mod remains disabled.

The build is compiled and independently checked against a pinned Valheim 1.0.14 reference assembly; its SHA-256 and MVID protect build provenance only. The paved-road change runs as Valheim enters place mode, before availability refresh, and re-discovers the active-table candidate immediately before vanilla requirement checks. It sets only the uniquely selected semantic Paved Road recipe's crafting-station reference to `null`, locally and reversibly; all resource and placement handling remains vanilla.

The core road bonuses have been live-tested in Valheim. The recipe-level stonecutter removal and multiplayer behavior still require live validation.

## Source and issues

https://github.com/JStack424/Treadwell

Licensed under the MIT License.
