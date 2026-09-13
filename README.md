# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus Valheim mod that makes constructed paths meaningfully easier to sprint along without changing the rest of movement.

## MVP behavior

- Vanilla dirt paths: **5% faster sprinting** and **5% less sprint stamina drain** by default.
- Vanilla paved roads: **10% faster sprinting** and **10% less sprint stamina drain** by default.
- Walking, sneaking, swimming, jumping, dodging, attacks, carts, NPCs, natural terrain, cultivated soil, and building floors are unchanged.
- A private 0.18-second natural-terrain gap hold smooths path-paint boundaries. Cultivated soil, building floors, leaving the ground, and other non-terrain surfaces clear the bonus immediately.
- No status icon, popup, sound, or gameplay message.

The patches multiply Valheim's calculated run-speed factor and its final status-effect-adjusted run-stamina drain. They do not replace base values, Run skill scaling, equipment modifiers, status-effect modifiers, or the global movement-stamina rate.

## Configuration

Treadwell creates exactly five settings in `BepInEx/config/com.jstack424.treadwell.cfg`:

1. `Enable mod` (default `true`)
2. `Dirt sprint speed bonus (%)` (default `5`)
3. `Dirt sprint stamina reduction (%)` (default `5`)
4. `Paved sprint speed bonus (%)` (default `10`)
5. `Paved sprint stamina reduction (%)` (default `10`)

Percentages are constrained to `0–100`. Settings are read live; the master switch installs or removes Treadwell's isolated Harmony patches.

## Compatibility and multiplayer

Version 0.1.0 is deliberately fail-closed for **Valheim 1.0.12 / Steam build 25253764**, Unity `6000.0.75f1`, BepInEx `5.4.23.5`, and Harmony `2.9.0.0`. It verifies the game assembly hash, MVID, every patched method, every accessed field, and the expected dirt/cultivated/paved paint encodings before installing hooks. A different runtime disables the feature and logs the reason.

Treadwell changes only the local player's movement calculations and writes no world or container state. Each player who wants the bonuses installs the mod on their own client. This first test build has not yet been exercised in a live game or multiplayer session.

## Local development

Private Valheim and BepInEx DLLs remain untracked and are never packaged.

```bash
./scripts/build.sh
./scripts/test-package.sh
```

The build performs locked restore, warning-as-error Release compilation, pure behavior tests, an independent metadata contract test against the pinned game assembly, repository checks, and package auditing. The test package contains exactly five files.

## Source and license

Source: https://github.com/JStack424/Treadwell (reserved package URL; no repository was created for this local MVP)

Licensed under the [MIT License](LICENSE).
