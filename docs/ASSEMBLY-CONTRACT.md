# Verified Valheim road-building, movement, and terrain contract

Inspected reference: Valheim `1.0.12`, Steam build `25253764`.

- `assembly_valheim.dll` SHA-256: `27a766a8d23a7bd8b6a54fb9ad0452a96c305fb3629b39c40527c09a1c393a84`
- Module MVID: `b8a6fd30-3061-43b3-99f2-11c2e315bc54`

## Paved-road placement

`Player.HaveRequirements(Piece, Player.RequirementMode) -> bool` checks the selected piece's crafting station before DLC, free-build, and material requirements. For `CanBuild`, the verified method contains exactly one consecutive call sequence from `CraftingStation.HaveBuildStationInRange(string, Vector3)` to Unity's object-to-boolean conversion. `Player.UpdatePlacement` uses this result before `TryPlacePiece`; after successful placement it calls the unchanged `ConsumeResources` path.

Treadwell inserts one fail-closed boolean adjustment directly after that unique station-range result. A false result changes to true only when the option is enabled and the request is `CanBuild` for the exact `paved_road` / `$piece_pavedroad` / `$piece_stonecutter` identity with a `TerrainModifier.PaintType.Paved` component. The remaining vanilla method still checks DLC, free-build, and every stone requirement. `IsKnown` and `CanAlmostBuild`, repairs/removals, other pieces, other stations, placement validity, resource consumption, tool stamina/durability, skills, stats, and effects are untouched.

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

Runtime startup validates the exact versions, SHA-256, MVID, method signatures, field signatures, parameter name used by the stamina postfix, and paint colors before installing the three patches. The station transpiler additionally refuses to install unless its call sequence occurs exactly once. A separate build-time metadata reader checks the same assembly contract and exact station call sequence without loading game code.
