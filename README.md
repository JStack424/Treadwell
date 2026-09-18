# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus Valheim mod that makes roads less tedious to build and more rewarding to use. Paved roads can be placed without repeatedly moving a stonecutter, while dirt paths and paved roads provide modest sprint bonuses.

## Features

- **Paved-road building:** place the vanilla paved-road terrain piece without a nearby stonecutter by default. Stone cost, placement rules, tool behavior, and every other piece remain vanilla.
- **Dirt paths:** 10% faster sprinting and 10% less sprint-stamina use by default.
- **Paved roads:** 20% faster sprinting and 20% less sprint-stamina use by default.
- All four percentages are independently configurable from 0% to 100%.
- Bonuses apply only to the local player while sprinting on vanilla terrain-painted paths.
- A short 0.18-second smoothing window prevents flicker across small gaps in road paint.
- Cultivated soil, building floors, leaving the ground, and other non-terrain surfaces clear the bonus immediately.
- No status icon, popup, sound, gameplay message, custom world state, or server requirement.

Walking, sneaking, swimming, jumping, dodging, attacks, carts, creatures, natural terrain, cultivated soil, and building floors are unchanged.

Treadwell multiplies Valheim's calculated run-speed factor and final status-effect-adjusted sprint-stamina drain. It does not replace base values or bypass Run skill, equipment, status-effect, or global movement-stamina modifiers.

## Installation

Treadwell requires [BepInExPack for Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

Install through r2modman or Thunderstore Mod Manager, or copy `Treadwell.dll` to:

```text
BepInEx/plugins/Treadwell/Treadwell.dll
```

Each player who wants the building convenience or movement bonuses installs Treadwell on their own client. A server installation is not required, and Treadwell does not synchronize configuration or write custom mod state into the world.

## Configuration

Treadwell creates exactly six settings in `BepInEx/config/com.jstack424.treadwell.cfg`:

1. `Enable mod` (default `true`)
2. `Paved roads without stonecutter` (default `true`)
3. `Dirt sprint speed bonus (%)` (default `10`)
4. `Dirt sprint stamina reduction (%)` (default `10`)
5. `Paved sprint speed bonus (%)` (default `20`)
6. `Paved sprint stamina reduction (%)` (default `20`)

The stonecutter option is read live. When enabled, Treadwell identifies exactly one semantic Paved Road entry in the active tool table: one root Piece with no additional child Pieces, one paved terrain operation (including child components), one attached crafting-station requirement, and one single-unit Stone resource requirement. The Paved Road entry's root, Piece, station, and localized display names are diagnostic evidence only, not identity gates; the expected Stone resource prefab is checked as part of the semantic shape. Treadwell then removes only that Piece's crafting-station reference locally. Turning the option off restores the exact captured object. It never changes the resource requirement, unlock knowledge, terrain checks, hoe behavior, repairs, or unrelated pieces and stations. Zero or multiple semantic matches fail closed and leave vanilla requirements intact; a one-time bounded BepInEx diagnostic lists the observed candidate shapes and names for troubleshooting.

All percentages are constrained to `0–100` and are read live. The master switch installs or removes Treadwell's isolated Harmony patches.

If an earlier test build created the configuration file, BepInEx may preserve its older defaults. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to `10`, `10`, `20`, and `20`.

## Compatibility and safety

Treadwell 0.1.2 uses a contract-based compatibility gate. Valheim's displayed version, Unity version, and loaded `assembly_valheim` MVID are logged as diagnostics, but they are not exact-version blockers. Client and dedicated-server assemblies, and compatible Valheim 1.0 patch releases, may differ in build identity while exposing the same APIs Treadwell needs.

Before installing anything, Treadwell verifies every Valheim method and overload it patches or calls, the matching Harmony prefix/postfix shapes, every accessed game field, the Unity component/property methods used for road discovery, and the expected piece-table, resource, enum, and terrain-paint contracts. Each required member must resolve to exactly one compatible signature. Missing or ambiguous members fail closed. Patch installation is transactional: if any Harmony patch fails, Treadwell removes every patch it installed, restores any owned Paved Road station override, and disables all features.

The build remains compiled and independently checked against the pinned Valheim `1.0.14` / Steam build `25364309` reference bundle. Its hashes and MVID document and protect build provenance only; they are deliberately not compared with the player's runtime assembly. The pinned IL verifies that `PieceTable.UpdateAvailable` reads each table entry's root `Piece`; discovery additionally searches children for the terrain operation. Treadwell applies the uniquely selected semantic Paved Road mutation when Valheim enters place mode, before piece-table availability refreshes, and re-discovers it immediately before vanilla requirement checks. Its `Piece.m_craftingStation` reference is set to `null` without depending on guessed piece, localization, or station identifiers. Valheim's normal recipe-knowledge and resource checks still decide whether the piece is unlocked and affordable. Disable, configuration changes, scene unload, and plugin unload restore the captured original reference. DLC, free-build, stone-count, placement, consumption, stamina, durability, skill, and effect handling remain vanilla.

The core road bonuses have been live-tested in Valheim. The recipe-level stonecutter removal and multiplayer behavior still require live validation.

## Development

Private Valheim and BepInEx assemblies remain untracked and are never packaged.

```bash
./scripts/build.sh
./scripts/test-package.sh
```

The build performs locked restore, warning-as-error Release compilation, pure behavior tests (including exact-contract acceptance/rejection and transactional rollback), an independent assembly-contract test against the pinned provenance assembly, reference fingerprint checks, repository checks, and an exact five-file package audit.

## Source, issues, and license

- Source and issue tracker: https://github.com/JStack424/Treadwell
- License: [MIT](LICENSE)
