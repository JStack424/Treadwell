# Treadwell

**Build roads worth taking.**

Treadwell gives Valheim's constructed roads a small, practical payoff while keeping movement vanilla-plus:

- Dirt paths: **+10% sprint speed** and **10% less sprint stamina use** by default.
- Paved roads: **+20% sprint speed** and **20% less sprint stamina use** by default.

Only the local player's sprinting on vanilla terrain-painted paths is changed. Natural terrain, cultivated soil, floors, walking, sneaking, swimming, jumping, dodging, attacks, carts, and creatures are untouched. A very short edge-smoothing window prevents road-paint boundaries from flickering, and there is no status icon or gameplay message.

## Configuration

Five settings are available in `BepInEx/config/com.jstack424.treadwell.cfg`:

- Master enable
- Dirt sprint-speed percentage
- Dirt sprint-stamina reduction percentage
- Paved sprint-speed percentage
- Paved sprint-stamina reduction percentage

All percentages are constrained to 0–100.

## Installation

Import the ZIP as a local mod in r2modman/Thunderstore Mod Manager, or extract `plugins/Treadwell/Treadwell.dll` into `BepInEx/plugins/Treadwell/`. BepInEx Pack for Valheim 5.4.2350 is required.

Each player who wants the bonuses installs Treadwell locally. No server installation or configuration synchronization is required for this MVP.

## Compatibility and safety

This 0.1.0 test build supports **Valheim 1.0.12 / Steam build 25253764**. It validates the exact runtime and movement/terrain APIs before installing either Harmony patch; an unverified game update disables the feature and logs the reason instead of guessing.

This package is a local test candidate and has not yet been verified in a live game or multiplayer session.

## Source

https://github.com/JStack424/Treadwell (reserved URL; repository not created for this local test build)
