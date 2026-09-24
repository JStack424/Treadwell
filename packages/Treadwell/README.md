# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus mod that makes Valheim's roads less tedious to build and more rewarding to use:

- **Adjustable terrain brush:** select the hoe's exact vanilla **Level Ground**, **Pathen**, or **Paved Road**, then hold **Left Alt or Right Alt** and scroll up/down to change both the visible brush and real terrain-effect radius in 0.5 m steps from 1–10 m.
- **Input isolation:** outside that eligible Alt+scroll chord, ordinary scrolling and placement rotation stay vanilla. Raise Ground, cultivator actions, removal mode, hammer pieces, and other or ambiguous actions are excluded.
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

Radius controls add no configuration setting. Each action keeps its own session-only radius, initialized to Level Ground 3 m, Pathen 2 m, and Paved Road 3 m, and changed only through Alt+scroll. Treadwell requires one unique exact vanilla match for all three actions in the active tool's piece table, including their prefab/piece identities, operation/recipe shapes, and 3/2/3 m baselines. Missing, changed, or ambiguous matches leave radius controls and scroll untouched.

The stonecutter option is read live. When enabled, Treadwell requires exactly one active-table entry with the semantic Paved Road shape: one root Piece with no additional child Pieces, one paved terrain operation (including child components), an attached crafting-station requirement, and one single-unit Stone resource requirement. The entry's root, Piece, station, and localized names are logged for diagnostics but are not identity gates; the expected Stone resource prefab is checked as part of the semantic shape. Treadwell removes only that Piece's station reference locally; disabling restores the exact captured object. Zero or multiple matches fail closed and leave vanilla requirements intact. Stone cost, unlock knowledge, hoe behavior, repairs, and unrelated pieces remain unchanged. All percentages are independently constrained to 0–100 and are read live.

If you used an earlier test build, BepInEx may retain its older values. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to 10, 10, 20, and 20.

## Compatibility and safety

Treadwell 0.2.0 uses a contract-based compatibility gate. Valheim's displayed version, Unity version, assembly hash, and MVID are not runtime allowlists, so compatible minor patches and client/server assembly variants are not rejected solely because their build identity differs. Before installing anything, Treadwell requires one exact match for every patched target and overload, matching Harmony patch methods, every accessed game field, and the Unity road-discovery and terrain contracts it needs. Missing, ambiguous, or changed contracts still disable Treadwell safely. A partial patch installation is rolled back before the mod remains disabled.

For radius controls, Treadwell accepts only the exact vanilla Level Ground, Pathen, and Paved Road entries, each with one root Piece, one root `TerrainOp`, no legacy `TerrainModifier`, no rotation, and the verified operation/recipe shape. It uniformly scales the placement ghost's active `_GhostOnly` marker, proportionally scales every enabled `TerrainOp.Settings` radius around Valheim's synchronous placement clone, preserves Paved Road's 2.2:3 paint-to-smooth ratio, and restores the original prefab fields in guaranteed finalizer cleanup. Disabled channels stay untouched. Tool/action changes, scene unload, disable, and exceptions clear transient state; conflicting third-party field changes are left untouched.

The build is compiled and independently checked against a pinned Valheim 1.0.14 reference assembly; its SHA-256 and MVID protect build provenance only. The paved-road change runs as Valheim enters place mode, before availability refresh, and re-discovers the active-table candidate immediately before vanilla requirement checks. It sets only the uniquely selected semantic Paved Road recipe's crafting-station reference to `null`, locally and reversibly; all resource and placement handling remains vanilla.

Treadwell 0.1.2's road bonuses and stonecutter-free Paved Road behavior have been live-tested successfully. The new 0.2.0 radius controls and visual/effect lockstep still require live validation. Because the terrain RPC carries the operation prefab identity rather than custom radius values, remote-owner and multiplayer behavior is explicitly unverified in this test build.

## Source and issues

https://github.com/JStack424/Treadwell

Licensed under the MIT License.
