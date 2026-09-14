# Verified Valheim road-building, movement, and terrain contract

Inspected reference: Valheim `1.0.12`, Steam build `25253764`.

- `assembly_valheim.dll` SHA-256: `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`
- Module MVID: `b8a6fd30-3061-43b3-99f2-11c2e315bc54`

## Paved-road recipe

The verified `Player.SetPlaceMode(PieceTable)` method assigns the active tool's table, calls private `Player.UpdateAvailablePiecesList()`, and that method calls `PieceTable.UpdateAvailable(HashSet<string>, Player, bool, bool)`. `Player.GetBuildTool() -> PieceTable` returns the full active table for live setting changes; unlike `GetBuildPieces()`, it is not limited to the currently selected category. `PieceTable.UpdateAvailable` refreshes availability from public `m_pieces : List<GameObject>`. Each entry carries its live `Piece` component, whose public `m_craftingStation : CraftingStation` field is Valheim's recipe-level station requirement. `ZNetScene.OnDestroy()` is the verified scene-lifecycle cleanup hook.

Treadwell searches that full table for only canonical `paved_road` / `$piece_pavedroad`; the canonicalizer accepts the ordinary Unity `paved_road(Clone)` runtime form but no lookalikes. It captures whatever non-null `CraftingStation` object is actually attached and sets only that `Piece.m_craftingStation` field to `null`. There is deliberately no station-prefab or station-display-name precondition: the failed earlier candidate could silently no-op when the runtime station object did not expose both guessed identifiers.

The mutation runs before `SetPlaceMode` triggers availability refresh, before every `PieceTable.UpdateAvailable`, and as a narrow prefix on `Player.HaveRequirements(Piece, RequirementMode)`. The last hook does not change the method result or skip any code; it only ensures the exact Paved Road field is null before vanilla evaluates it. Pinned IL verifies that `HaveRequirements` gates station knowledge/range only inside the non-null `m_craftingStation` branch, and that `Player.UpdatePlacement` calls this method before `TryPlacePiece`. The normal Paved Road recipe-knowledge and resource checks therefore still run, while build-list, HUD, and placement paths observe the stationless recipe.

Disabling the setting, disabling the module, destroying the scene `ZNetScene`, or unloading the plugin restores the captured original object if the field is still `null`. If another runtime component replaces the field while Treadwell owns the override, cleanup does not overwrite that external change. Piece-table replacement restores the old piece before changing the new exact match. Stone requirements, unlock knowledge, repairs/removals, other pieces, placement validity, resource consumption, tool stamina/durability, skills, stats, and effects remain vanilla.

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

Runtime startup validates the exact versions, SHA-256, MVID, place-mode, piece-table, requirement, placement, and lifecycle method signatures, field signatures, parameter name used by the stamina postfix, and paint colors before installing the six hooks. A separate build-time metadata reader checks the same assembly contract and the critical IL call/field-read edges without loading game code. Treadwell performs no runtime recipe mutation if any pinned contract check fails.
