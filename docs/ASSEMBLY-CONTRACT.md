# Verified Valheim road-building, movement, and terrain contract

Inspected reference: Valheim `1.0.14`, Steam build `25364309`.

- `assembly_valheim.dll` SHA-256: `e5af0669755ed3b098f71b4dd0753f8a997761b99bca1e8dac3d5ca4c706a0be`
- Module MVID: `a63433e8-968e-407a-918a-9f9fe7e7ba9a`

## Paved-road recipe

The verified `Player.SetPlaceMode(PieceTable)` method assigns the active tool's table, calls private `Player.UpdateAvailablePiecesList()`, and that method calls `PieceTable.UpdateAvailable(HashSet<string>, Player, bool, bool)`. `Player.GetBuildTool() -> PieceTable` returns the full active table for live setting changes; unlike `GetBuildPieces()`, it is not limited to the currently selected category. `PieceTable.UpdateAvailable` refreshes availability from public `m_pieces : List<GameObject>`. The pinned IL explicitly calls `GameObject.GetComponent<Piece>()` on each table entry, establishing that the live Piece is attached to the entry root. `Piece.m_craftingStation`, `Piece.m_resources`, `Piece.Requirement.m_resItem`, and `Piece.Requirement.m_amount` are all public fields in this build. `ZNetScene.OnDestroy()` is the verified scene-lifecycle cleanup hook.

Treadwell no longer guesses Paved Road from prefab or localization identifiers. It inspects every entry and its children, then requires exactly one Piece component and requires it on the table-entry root, exactly one `TerrainModifier.PaintType.Paved` operation, an attached crafting-station requirement (or the exact station reference already removed and owned by Treadwell), and exactly one valid single-unit Stone resource requirement. Only one matching entry is accepted. Zero or multiple matches fail closed, restore any owned override, and emit one bounded diagnostic containing sanitized root/piece/station names, terrain paint types, and resource summaries. Root, Piece, table, station, and localized names are evidence for troubleshooting only and never decide eligibility; the expected Stone resource prefab is the narrow name-based resource constraint.

The mutation runs before `SetPlaceMode` triggers availability refresh, before every `PieceTable.UpdateAvailable`, and as a narrow prefix on `Player.HaveRequirements(Piece, RequirementMode)`. The last hook re-discovers from the player's active tool table rather than trusting an arbitrary Piece argument; it does not change the method result or skip code. Pinned IL verifies that `HaveRequirements` gates station knowledge/range only inside the non-null `m_craftingStation` branch, and that `Player.UpdatePlacement` calls this method before `TryPlacePiece`. The normal Paved Road recipe-knowledge and resource checks therefore still run, while build-list, HUD, and placement paths observe the stationless recipe.

Disabling the setting, disabling the module, destroying the scene `ZNetScene`, or unloading the plugin restores the captured original object if the field is still `null`. If another runtime component replaces the field while Treadwell owns the override, cleanup does not overwrite that external change. Piece-table replacement restores the old piece before changing a newly selected unique semantic match. Stone requirements, unlock knowledge, repairs/removals, other pieces, placement validity, resource consumption, tool stamina/durability, skills, stats, and effects remain vanilla.

## Adjustable terrain radius

The verified private `Player.UpdatePlacement(bool, float)` method owns placement input and calls public static `ZInput.GetMouseScrollWheel()`. Public `PieceTable.GetSelectedPiece() -> Piece` exposes the active action. `Player.m_placementGhost : GameObject` is the separate preview instance; verified `Player.SetupPlacementGhost()` destroys its `TerrainModifier` components, activates the `_GhostOnly` child, and preserves the selected prefab's root scale. Treadwell therefore scales only `_GhostOnly` in X/Z for visualization and never expects the preview to execute terrain logic.

The eligible runtime set is deliberately semantic and narrow. The active tool's piece table must contain exactly one of each: a zero-resource `TerrainModifier.m_level` operation (Level Ground), a zero-resource `m_paintCleared` + `PaintType.Dirt` operation (Pathen), and the existing crafting-station-backed single-unit Stone + `PaintType.Paved` operation (Paved Road). Every accepted entry must have exactly one `Piece` on its table-entry root, exactly one `TerrainModifier`, and `TerrainModifier.GetRadius() == 2f`. That unique three-operation set is the hoe-table gate; recognition uses no guessed table, piece, or localization identifiers. All three matches must be unique or the radius feature fails closed for that table. This excludes Raise Ground's resource-backed level operation, Cultivate/Reset paint, hammer pieces, removal mode, unrelated hoe actions, and ambiguous modded duplicates.

Radius is a shared session value initialized to 2 m, quantized in 0.5 m steps, and clamped to 1–10 m. While Left Alt or Right Alt is physically held and an eligible brush has a synchronized `_GhostOnly` marker, Treadwell reads the wheel in an `UpdatePlacement` prefix and suppresses later wheel reads only for that frame. No eligible action or no Alt means no suppression, preserving ordinary placement rotation and other scroll consumers.

Public `TerrainModifier` fields `m_level`, `m_levelRadius`, `m_smooth`, `m_smoothRadius`, `m_paintCleared`, `m_paintType`, and `m_paintRadius`, plus public `GetRadius()`, are verified. The pinned `Player.PlacePiece(Piece, Vector3, Quaternion, bool, bool)` IL calls `TerrainModifier.SetTriggerOnPlaced(true)`, synchronously `Object.Instantiate<GameObject>` on the selected prefab, and then `SetTriggerOnPlaced(false)`. Treadwell wraps this exact method: its prefix captures and proportionally changes only the selected eligible prefab's active level/smooth/paint radius fields; the clone receives those values and performs the terrain operation; a Harmony finalizer conditionally restores the captured originals whether placement returns or throws. Restoration never overwrites a field that another runtime participant changed during the call. Actual radius changes are refused unless the corresponding indicator synchronized successfully. Tool/action changes, hidden placement input, scene destruction, plugin disable, and input exceptions restore visual scale or clear transient ownership.

## Terrain

`TerrainModifier.PaintType` identifies `Dirt = 0`, `Cultivate = 1`, and `Paved = 2`. `Heightmap` stores these in the paint mask as public static colors:

- `m_paintMaskDirt = (1, 0, 0, 1)`
- `m_paintMaskCultivated = (0, 1, 0, 1)`
- `m_paintMaskPaved = (0, 0, 1, 1)`

`Heightmap.GetPaintMask(Vector3) -> Color` converts the world position to a paint-mask vertex and returns that pixel. `FootStep.GroundMaterial` is not used because it does not preserve a distinct dirt-vs-paved classification.

Treadwell asks `Character.GetLastGroundCollider() -> Collider`, requires a `Heightmap` on that collider or its parent, and samples the local player's world position. A floor collider therefore cannot inherit a painted road underneath it.

## Sprint speed

`Player.GetRunSpeedFactor() -> float` is a protected virtual method. In the verified IL it computes Run-skill and equipment movement scaling. `Character.UpdateWalking(float)` calls it only in the running branch, then continues through Valheim's ordinary movement/status-effect processing.

Treadwell applies one postfix to `Player.GetRunSpeedFactor` and multiplies that vanilla factor only for the local player while `IsRunning()` is true. Valheim's downstream movement and status-effect processing continues unchanged.

## Sprint stamina

`SEMan.ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir, bool minZero) -> void` applies every active status effect to the already skill/equipment-adjusted run drain. `Player.CheckRun(Vector3, float)` then multiplies that result by frame time and `Game.m_moveStaminaRate` before calling `UseStamina`.

Treadwell applies one postfix to `SEMan.ModifyRunStaminaDrain` and multiplies the final `drain` reference. Harmony injects the owning private `SEMan.m_character : Character`; the postfix proceeds only when it is the local `Player`. Because this method is the dedicated run-drain path, no jump, dodge, attack, swim, sneak, or other stamina cost is touched.

## Fail-closed checks

The SHA-256, MVID, Steam build, and version above identify the private assembly used for reproducible compilation and offline IL verification. They are provenance, not runtime allowlists: Valheim client and dedicated-server assemblies can legitimately have different identities while exposing the same compatible API.

Runtime startup instead requires exactly one match for every patched target and overload, every Harmony prefix/postfix/finalizer shape, every accessed game field, the Unity component/object/equality/property/input/transform methods and color channels used for road discovery, brush scaling, and terrain sampling, the exact Harmony constructors and patch/unpatch/reflection APIs emitted into the plugin, the expected paint enum values and colors, and the parameter shape used by the stamina postfix. Missing, ambiguous, or changed contracts disable Treadwell before Harmony is constructed or installation begins. Patch installation is transactional; a failure runs every rollback step, unpatches the module, clears gameplay entrypoints, and restores any owned Paved Road station override before the plugin remains disabled. If an override setter throws, its captured state is retained so the host can retry cleanup safely. A separate build-time metadata reader still pins this reference assembly and verifies critical IL call/field-read edges without loading game code. Executable core tests also exercise exact contract acceptance, mismatched/ambiguous rejection, and rollback behavior without loading the game.
