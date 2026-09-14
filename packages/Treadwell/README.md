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

The stonecutter option is read live. When enabled, Treadwell removes whatever non-null crafting-station reference is attached to only the exact vanilla Paved Road entry in the active hoe piece table, including Valheim's harmless `(Clone)` runtime name. Disabling it restores the exact captured object. Stone cost, unlock knowledge, hoe behavior, repairs, and unrelated pieces remain unchanged. BepInEx logs one concise diagnostic stating whether the live field was removed, was already absent, or no exact candidate was found. All percentages are independently constrained to 0–100 and are read live.

If you used an earlier test build, BepInEx may retain its older values. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to 10, 10, 20, and 20.

## Compatibility and safety

Treadwell 0.1.1 supports exactly **Valheim 1.0.12 / Steam build 25253764**. It validates the pinned runtime, piece-table and movement hooks, terrain APIs, and field contracts before installing its Harmony patches. The paved-road change runs as Valheim enters place mode, before availability refresh, and immediately before vanilla requirement checks. It sets only the exact vanilla Paved Road recipe's crafting-station reference to `null`, locally and reversibly; all resource and placement handling remains vanilla. An unverified game update or changed contract disables Treadwell instead of guessing.

The core road bonuses have been live-tested in Valheim. The recipe-level stonecutter removal and multiplayer behavior still require live validation, and future game versions are not assumed compatible.

## Source and issues

https://github.com/JStack424/Treadwell

Licensed under the MIT License.
