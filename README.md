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

The stonecutter option is read live. When enabled, Treadwell finds the exact vanilla Paved Road entry in the active hoe piece table, including Valheim's harmless `(Clone)` runtime name, and removes whatever non-null crafting-station reference is attached locally. Turning the option off restores the exact captured object. It never changes the paved road's stone cost, unlock knowledge, terrain checks, hoe behavior, repairs, or unrelated pieces and stations. BepInEx logs one concise diagnostic when the live field is removed, was already absent, or no exact Paved Road candidate can be found.

All percentages are constrained to `0–100` and are read live. The master switch installs or removes Treadwell's isolated Harmony patches.

If an earlier test build created the configuration file, BepInEx may preserve its older defaults. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to `10`, `10`, `20`, and `20`.

## Compatibility and safety

Treadwell 0.1.1 supports exactly:

- Valheim `1.0.12` / Steam build `25253764`
- Unity `6000.0.75f1`
- BepInEx `5.4.23.5` (distributed by BepInExPack for Valheim `5.4.2350`)
- Harmony `2.9.0.0`

At startup, Treadwell verifies the game assembly hash and MVID, every patched method, every accessed field, and the expected piece-table and terrain-paint contracts before installing gameplay hooks. It applies the exact Paved Road mutation when Valheim enters place mode, before piece-table availability refreshes, and again immediately before vanilla requirement checks. Only the exact `paved_road` / `$piece_pavedroad` component is eligible; its `Piece.m_craftingStation` reference is set to `null` without depending on the runtime station object's prefab or display name. Valheim's normal recipe-knowledge and resource checks still decide whether the piece is unlocked and affordable. Disable, configuration changes, scene unload, and plugin unload restore the captured original reference. DLC, free-build, stone-count, placement, consumption, stamina, durability, skill, and effect handling remain vanilla. If the runtime contract does not match the pinned build, Treadwell disables itself instead of guessing.

The core road bonuses have been live-tested in Valheim. The recipe-level stonecutter removal and multiplayer behavior still require live validation, and future Valheim versions are not assumed compatible until a new build is checked.

## Development

Private Valheim and BepInEx assemblies remain untracked and are never packaged.

```bash
./scripts/build.sh
./scripts/test-package.sh
```

The build performs locked restore, warning-as-error Release compilation, pure behavior tests, an independent assembly-contract test against the pinned game assembly, reference fingerprint checks, repository checks, and an exact five-file package audit.

## Source, issues, and license

- Source and issue tracker: https://github.com/JStack424/Treadwell
- License: [MIT](LICENSE)
