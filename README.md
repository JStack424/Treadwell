# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus Valheim mod that rewards road building with modest sprint bonuses. Dirt paths and paved roads become faster and more stamina-efficient while the rest of Valheim's movement stays unchanged.

## Features

- **Dirt paths:** 10% faster sprinting and 10% less sprint-stamina use by default.
- **Paved roads:** 20% faster sprinting and 20% less sprint-stamina use by default.
- All four percentages are independently configurable from 0% to 100%.
- Bonuses apply only to the local player while sprinting on vanilla terrain-painted paths.
- A short 0.18-second smoothing window prevents flicker across small gaps in road paint.
- Cultivated soil, building floors, leaving the ground, and other non-terrain surfaces clear the bonus immediately.
- No status icon, popup, sound, gameplay message, world-state change, or server requirement.

Walking, sneaking, swimming, jumping, dodging, attacks, carts, creatures, natural terrain, cultivated soil, and building floors are unchanged.

Treadwell multiplies Valheim's calculated run-speed factor and final status-effect-adjusted sprint-stamina drain. It does not replace base values or bypass Run skill, equipment, status-effect, or global movement-stamina modifiers.

## Installation

Treadwell requires [BepInExPack for Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

Install through r2modman or Thunderstore Mod Manager, or copy `Treadwell.dll` to:

```text
BepInEx/plugins/Treadwell/Treadwell.dll
```

Each player who wants the bonuses installs Treadwell on their own client. A server installation is not required, and Treadwell does not synchronize configuration or write mod state into the world.

## Configuration

Treadwell creates exactly five settings in `BepInEx/config/com.jstack424.treadwell.cfg`:

1. `Enable mod` (default `true`)
2. `Dirt sprint speed bonus (%)` (default `10`)
3. `Dirt sprint stamina reduction (%)` (default `10`)
4. `Paved sprint speed bonus (%)` (default `20`)
5. `Paved sprint stamina reduction (%)` (default `20`)

All percentages are constrained to `0–100` and are read live. The master switch installs or removes Treadwell's isolated Harmony patches.

If an earlier test build created the configuration file, BepInEx may preserve its older defaults. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to `10`, `10`, `20`, and `20`.

## Compatibility and safety

Treadwell 0.1.0 supports exactly:

- Valheim `1.0.12` / Steam build `25253764`
- Unity `6000.0.75f1`
- BepInEx `5.4.23.5` (distributed by BepInExPack for Valheim `5.4.2350`)
- Harmony `2.9.0.0`

At startup, Treadwell verifies the game assembly hash and MVID, every patched method, every accessed field, and the expected dirt, cultivated, and paved paint encodings before installing gameplay hooks. If the runtime does not match this pinned contract, Treadwell disables itself and logs the reason instead of guessing.

The core road bonuses have been live-tested in Valheim. Multiplayer behavior has not yet been independently verified, and future Valheim versions are not assumed compatible until a new build is checked.

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
