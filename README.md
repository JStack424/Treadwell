# Treadwell

**Build roads worth taking.**

Treadwell is a focused, vanilla-plus Valheim mod that makes roads less tedious to build and more rewarding to use. Level Ground, Pathen, and Paved Road can use a larger or smaller synchronized terrain brush, paved roads can be placed without repeatedly moving a stonecutter, and dirt paths and paved roads provide modest sprint bonuses.

> **0.2.5 is a test candidate.** Version 0.2.3’s adjustable terrain brushes were live-confirmed as working really well. Version 0.2.4’s added hint did not appear because it cloned Valheim’s gamepad-only alternative-placement label beneath the inactive Gamepad branch. Version 0.2.5 keeps the proven gameplay path and places Treadwell’s owned clone in the validated Keyboard layout instead.

## Features

- **Adjustable terrain brush:** with the hoe's exact vanilla **Level Ground**, **Pathen**, or **Paved Road** action selected, hold **Left Alt or Right Alt** and scroll up/down in 0.5 m steps. Level Ground ranges from 1–8 m; Pathen and Paved Road range from 1–10 m. The visible `_GhostOnly` brush marker and the placed terrain operation use the same proportional scale.
- **Built-in control hint:** the persistent keyboard placement row adds **Alt + Scroll — Change Size** only while one of those three verified terrain actions is selected and the piece selector is closed. It is hidden for Raise Ground, unrelated or changed pieces, invalid/ambiguous brush discovery, the open piece selector, and controller input.
- **Input isolation:** while the vanilla hoe is out in placement mode, Alt+scroll does not zoom the camera—even at a radius bound or with an ineligible hoe action selected. Without Alt, with another tool, or while the build-selection UI is open, ordinary camera zoom remains unchanged. Build-menu selection, placement rotation, controllers, and unrelated tools remain vanilla.
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

Terrain radius intentionally adds no configuration setting. Each action keeps its own independent session-only radius, initialized to its verified vanilla outer radius: Level Ground 3 m, Pathen 2 m, and Paved Road 3 m. Level Ground is constrained to 1–8 m; Pathen and Paved Road are constrained to 1–10 m. The runtime requires one unique complete three-brush set in the active tool's piece table, using each action's exact localized identity, operation shape, recipe fingerprint, and 3/2/3 m baseline. Runtime prefab object names are diagnostic rather than identity gates, which corrects Pathen recognition when Valheim's live object name differs. Zero, missing, changed, or ambiguous matches fail closed without changing the terrain effect.

The stonecutter option is read live. When enabled, Treadwell identifies exactly one semantic Paved Road entry in the active tool table: one root Piece with no additional child Pieces, one paved terrain operation (including child components), one attached crafting-station requirement, and one single-unit Stone resource requirement. The Paved Road entry's root, Piece, station, and localized display names are diagnostic evidence only, not identity gates; the expected Stone resource prefab is checked as part of the semantic shape. Treadwell then removes only that Piece's crafting-station reference locally. Turning the option off restores the exact captured object. It never changes the resource requirement, unlock knowledge, terrain checks, hoe behavior, repairs, or unrelated pieces and stations. Zero or multiple semantic matches fail closed and leave vanilla requirements intact; a one-time bounded BepInEx diagnostic lists the observed candidate shapes and names for troubleshooting.

All percentages are constrained to `0–100` and are read live. The master switch installs or removes Treadwell's isolated Harmony patches.

If an earlier test build created the configuration file, BepInEx may preserve its older defaults. Delete `BepInEx/config/com.jstack424.treadwell.cfg` once to regenerate the current defaults, or set the four percentages manually to `10`, `10`, `20`, and `20`.

## Compatibility and safety

Treadwell 0.2.5 test uses a contract-based compatibility gate. Valheim's displayed version, Unity version, and loaded `assembly_valheim` MVID are logged as diagnostics, but they are not exact-version blockers. Client and dedicated-server assemblies, and compatible Valheim 1.0 patch releases, may differ in build identity while exposing the same APIs Treadwell needs.

Before installing gameplay patches, Treadwell verifies every required Valheim method and overload it patches or calls, the matching Harmony prefix/postfix shapes, every accessed game field, the Unity component/property methods used for road discovery, and the expected piece-table, resource, enum, and terrain-paint contracts. Each required gameplay member must resolve to exactly one compatible signature; missing or ambiguous members fail closed. Gameplay patch installation is transactional. The hint's UI/input contract and Harmony patch are checked and installed separately: if that optional surface changes or fails, Treadwell skips or removes only its owned hint while terrain-radius gameplay remains active.

Radius controls recognize only Valheim's exact localized Level Ground, Pathen, and Paved Road actions with their verified semantic shapes. Prefab object names (`mud_road_v2`, `path_v2`, and `paved_road_v2`) are retained as bounded diagnostics, not brittle identity gates. Each action must have exactly one root `Piece`, one root `TerrainOp`, no legacy `TerrainModifier`, no rotation, and the verified operation/recipe shape. Level Ground is smooth + Dirt paint at 3 m; Pathen is Dirt paint at 2 m; Paved Road is 3 m smooth + 2.2 m Paved paint, with its station-backed one-Stone recipe. Treadwell scales the separate placement ghost's `_GhostOnly` geometry uniformly in XYZ even while that vanilla child is inactive. Pathen's visible particle is nested beneath its rotated 4×4×1 marker and uses Unity's Local particle-scaling mode, so Treadwell also scales that particle's own transform; particles attached directly to `_GhostOnly` are not double-scaled.

During the exact local `Player.PlacePiece` call, Treadwell temporarily scales the selected prefab so `TerrainOp.Awake` searches the full chosen area. It then intercepts the exact `TerrainOp.Settings` instance entering `TerrainComp.DoOperation`, after the terrain owner has resolved the operation identity, and temporarily applies the selected radius to that final settings object. The patch proceeds only for the active placement position and exact vanilla Level Ground, Pathen, or Paved Road semantic shape. A Harmony finalizer restores the final settings after the terrain operation and grass reset complete. Paved Road's 2.2:3 paint-to-smooth ratio is preserved, disabled channels are never changed, and conflicting third-party changes are never overwritten. Changing tools/actions, closing input, scene unload, plugin disable, or any input exception restores or clears transient state.

The build remains compiled and independently checked against the pinned Valheim `1.0.14` / Steam build `25364309` reference bundle. Its hashes and MVID document and protect build provenance only; they are deliberately not compared with the player's runtime assembly. The pinned IL verifies that `PieceTable.UpdateAvailable` reads each table entry's root `Piece`; discovery additionally searches children for the terrain operation. Treadwell applies the uniquely selected semantic Paved Road mutation when Valheim enters place mode, before piece-table availability refreshes, and re-discovers it immediately before vanilla requirement checks. Its `Piece.m_craftingStation` reference is set to `null` without depending on guessed piece, localization, or station identifiers. Valheim's normal recipe-knowledge and resource checks still decide whether the piece is unlocked and affordable. Disable, configuration changes, scene unload, and plugin unload restore the captured original reference. DLC, free-build, stone-count, placement, consumption, stamina, durability, skill, and effect handling remain vanilla.

Treadwell 0.1.2's road bonuses and stonecutter-free Paved Road behavior have been live-tested successfully. In the 0.2.x test line, Alt-wheel camera-zoom isolation has positive live confirmation. Live tests of both 0.2.1 and 0.2.2 showed that Level Ground's marker could resize while its real terrain effect stayed at the vanilla radius; Pathen's real effect also remained unchanged in 0.2.1. **Version 0.2.3 was live-confirmed as working really well**, including the corrected actual terrain-radius path. Version 0.2.4 attempted to add the native-style **Alt + Scroll — Change Size** discoverability hint, but cloned `m_buildAlternativePlacingKey` under its existing Gamepad parent. Valheim hides that branch for keyboard controls, while Treadwell hides the keyboard-only hint for gamepad controls, so it could never become visible. Version 0.2.5 validates the sibling Gamepad/Keyboard layout contract and attaches its isolated clone to the Keyboard container without changing Valheim's source label or revalidating gameplay from the HUD; the corrected visual still requires live confirmation. The terrain RPC carries the operation prefab identity rather than custom radius values, so an unmodded remote terrain owner cannot receive the custom radius; multiplayer/remote-owner behavior remains explicitly unverified.

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
