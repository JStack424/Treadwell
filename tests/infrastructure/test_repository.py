from __future__ import annotations
import json
from pathlib import Path
import re
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
META = json.loads((ROOT / "mod.json").read_text())
IDENTIFIER = META["identifier"]
PLUGIN_DIR = ROOT / "src" / IDENTIFIER
PLUGIN_PROJECT = PLUGIN_DIR / f"{IDENTIFIER}.csproj"
CORE_PROJECT = ROOT / "src" / f"{IDENTIFIER}.Core" / f"{IDENTIFIER}.Core.csproj"
TEST_PROJECT = ROOT / "tests" / f"{IDENTIFIER}.Tests" / f"{IDENTIFIER}.Tests.csproj"
COMPAT_PROJECT = ROOT / "tests" / f"{IDENTIFIER}.Compatibility.Tests" / f"{IDENTIFIER}.Compatibility.Tests.csproj"


class RepositoryInfrastructureTests(unittest.TestCase):
    def test_metadata_is_single_source_and_rendered_outputs_are_current(self):
        result = subprocess.run(["python3", "scripts/render_metadata.py", "--check"], cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, META["schema_version"])

    def test_framework_and_dependency_boundaries(self):
        plugin = PLUGIN_PROJECT.read_text()
        core = CORE_PROJECT.read_text()
        tests = TEST_PROJECT.read_text()
        compatibility_tests = COMPAT_PROJECT.read_text()
        self.assertIn("<TargetFramework>net48</TargetFramework>", plugin)
        self.assertIn("<TargetFramework>netstandard2.0</TargetFramework>", core)
        self.assertIn("<TargetFramework>net8.0</TargetFramework>", tests)
        self.assertIn("<TargetFramework>net8.0</TargetFramework>", compatibility_tests)
        self.assertIn("ProjectReference", tests)
        self.assertNotIn("ProjectReference", compatibility_tests)
        self.assertNotIn("PackageReference", compatibility_tests)
        self.assertNotIn("ProjectReference", plugin)
        self.assertIn("../$(ModIdentifier).Core/**/*.cs", plugin)
        self.assertNotRegex(core, r"BepInEx|UnityEngine|Harmony")

    def test_all_private_references_are_build_only_and_checked(self):
        project = PLUGIN_PROJECT.read_text()
        required = [line.strip() for line in (ROOT / "scripts/required-references.txt").read_text().splitlines() if line.strip()]
        fingerprints = json.loads((ROOT / "scripts/reference-fingerprints.json").read_text())["files"]
        self.assertEqual(sorted(required), sorted(fingerprints))
        self.assertEqual(len(required), project.count("<Private>false</Private>"))
        for name in required:
            self.assertIn(f"$(ValheimReferencePath)/{name}", project)
            self.assertIn(f"Exists('$(ValheimReferencePath)/{name}')", project)

    def test_private_artifacts_are_ignored_and_untracked(self):
        ignore = (ROOT / ".gitignore").read_text()
        self.assertIn("lib/local/", ignore)
        self.assertIn("*.dll", ignore)
        tracked = subprocess.check_output(["git", "ls-files", "*.dll", "*.pdb"], cwd=ROOT, text=True).strip()
        self.assertEqual("", tracked)

    def test_contract_gate_precedes_features_and_runtime_identity_is_diagnostic_only(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text()
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text()
        for forbidden in (
            "SupportedValheimMvid", "SupportedValheimSha256", "SupportedGameVersion", "SupportedUnityVersion",
            "SupportedBepInExVersion", "SupportedHarmonyVersion", "SHA256.Create", "File.OpenRead",
        ):
            self.assertNotIn(forbidden, gate)
        for diagnostic in ("RuntimeDiagnostics", "Version.CurrentVersion", "Application.unityVersion", "ModuleVersionId"):
            self.assertIn(diagnostic, gate)
        self.assertLess(plugin.index("CompatibilityGate.Evaluate"), plugin.index("_features.Start"))
        self.assertIn("Compatibility gate passed: \" + compatibility.Reason", plugin)
        self.assertIn("before any gameplay hooks were installed", plugin)

    def test_runtime_contract_requires_unique_exact_shapes_and_transactional_rollback(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text()
        module = (PLUGIN_DIR / "FeatureModule.cs").read_text()
        host = (PLUGIN_DIR / "FeatureHost.cs").read_text()
        for marker in (
            "matches.Length != 1", "ParametersMatch", "RequireMethod", "RequireMethodNamedReturn",
            "RequirePatchMethod", "RequireField", "RequireProperty", "RequireGenericMethod", "RequireEnumValue",
        ):
            self.assertIn(marker, gate)
        for patch in (
            "PlayerSetPlaceModePrefix", "PieceTableUpdateAvailablePrefix", "PlayerHaveRequirementsPrefix",
            "ZNetSceneOnDestroyPrefix", "GetRunSpeedFactorPostfix", "ModifyRunStaminaDrainPostfix",
        ):
            self.assertIn("RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(" + patch + ")", module)
        self.assertIn("_harmony.UnpatchSelf()", module)
        self.assertIn("cleanupFailures.Insert(0, installException)", module)
        self.assertIn("foreach (var module in _modules.Reverse())", host)

    def test_feature_modules_own_enable_disable_and_compatibility(self):
        module = (PLUGIN_DIR / "FeatureModule.cs").read_text()
        host = (PLUGIN_DIR / "FeatureHost.cs").read_text()
        for marker in ("ValidateCompatibility", "void Enable()", "void Disable()", "ConfigEntry<bool> Enabled"):
            self.assertIn(marker, module)
        self.assertIn("new Harmony(Plugin.PluginGuid + \".feature.\" + id)", module)
        self.assertIn("_harmony.UnpatchSelf()", module)
        self.assertIn("foreach (var module in _modules.Reverse())", host)

    def test_feature_scope_and_exact_six_settings(self):
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text()
        module = (PLUGIN_DIR / "FeatureModule.cs").read_text()
        core = (ROOT / "src" / f"{IDENTIFIER}.Core" / "RoadLogic.cs").read_text()
        station_override = (ROOT / "src" / f"{IDENTIFIER}.Core" / "PavedRoadStationOverride.cs").read_text()
        self.assertEqual(6, plugin.count("Config.Bind("))
        for marker in (
            '"Enable mod", true',
            '"Paved roads without stonecutter", true',
            '"Dirt sprint speed bonus (%)", 10f',
            '"Dirt sprint stamina reduction (%)", 10f',
            '"Paved sprint speed bonus (%)", 20f',
            '"Paved sprint stamina reduction (%)", 20f',
            "AcceptableValueRange<float>(0f, 100f)",
        ):
            self.assertIn(marker, plugin)
        self.assertIn('typeof(PieceTable), "UpdateAvailable"', module)
        self.assertIn("PieceTableUpdateAvailablePrefix", module)
        self.assertNotIn("PieceTableUpdateAvailablePostfix", module)
        self.assertIn("RefreshPlayerAvailablePieces", module)
        self.assertIn('typeof(Player), "SetPlaceMode"', module)
        self.assertIn("PlayerSetPlaceModePrefix", module)
        self.assertIn('typeof(Player), "GetBuildTool"', module)
        self.assertIn('typeof(Player), "UpdateAvailablePiecesList"', module)
        self.assertIn('typeof(Player), "HaveRequirements"', module)
        self.assertIn("PlayerHaveRequirementsPrefix", module)
        self.assertIn('typeof(ZNetScene), "OnDestroy"', module)
        self.assertIn("ZNetSceneOnDestroyPrefix", module)
        self.assertNotIn('typeof(Player), "GetBuildPieces"', module)
        self.assertIn('typeof(PieceTable), "m_pieces"', module)
        self.assertIn("piece.m_craftingStation = station", module)
        self.assertIn("_setStation(piece, null)", station_override)
        self.assertIn("_setStation(piece, originalStation)", station_override)
        self.assertIn("GetComponent<Piece>()", module)
        self.assertIn("GetComponentsInChildren<Piece>(true)", module)
        self.assertIn("GetComponentsInChildren<TerrainModifier>(true)", module)
        self.assertIn("PavedRoadCandidateSelector.Select", module)
        self.assertIn("HasRootPiece", station_override)
        self.assertIn("PavedTerrainModifierCount == 1", station_override)
        self.assertIn("ResourceRequirementCount == 1", station_override)
        self.assertIn("SingleUnitResourceRequirementCount == 1", station_override)
        self.assertIn("SingleUnitStoneResourceRequirementCount == 1", station_override)
        combined = module + station_override
        for removed in ("HaveRequirementsTranspiler", "AdjustStationSatisfied", "ResolveStationSatisfied", "ShouldIgnoreStationRange", "HaveBuildStationInRange"):
            self.assertNotIn(removed, combined)
        self.assertIn('typeof(Player), "GetRunSpeedFactor"', module)
        self.assertIn('typeof(SEMan), "ModifyRunStaminaDrain"', module)
        self.assertIn("__result *=", module)
        self.assertIn("drain *=", module)
        self.assertIn("player != Player.m_localPlayer", module)
        self.assertIn("player.IsOnGround()", module)
        self.assertIn("GetLastGroundCollider", module)
        self.assertIn("GetComponentInParent<Heightmap>", module)
        self.assertIn("TerrainSurface.Cultivated", core)
        self.assertIn("TerrainSurface.NonTerrain", core)
        self.assertIn("NaturalGapHoldSeconds = 0.18d", module)
        self.assertIn('string.Equals(name, "Stone", StringComparison.Ordinal)', module)
        self.assertIn('string.Equals(name, "Stone(Clone)", StringComparison.Ordinal)', module)
        for removed_identity_gate in (
            "VanillaPrefabName", "VanillaDisplayName", "RuntimeCloneSuffix", "IsExactPavedRoad",
            "VanillaStationPrefabName", "VanillaStationDisplayName", "IsExactStonecutter",
            "_stationPrefabName", "_stationDisplayName",
        ):
            self.assertNotIn(removed_identity_gate, station_override + module)
        for diagnostic in (
            "Semantic Paved Road candidate found",
            "station field removed",
            "Paved Road semantic discovery was",
            "Candidate shapes",
        ):
            self.assertIn(diagnostic, module)
        self.assertNotRegex(module, r"MessageHud|Hud\.instance|ShowMessage|StatusEffect")

    def test_pinned_provenance_is_checked_offline_and_runtime_contract_is_shape_based(self):
        build = (ROOT / "scripts" / "build.sh").read_text()
        compatibility_test = (COMPAT_PROJECT.parent / "Program.cs").read_text()
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text()
        module = (PLUGIN_DIR / "FeatureModule.cs").read_text()
        self.assertIn(f"tests/$identifier.Compatibility.Tests/$identifier.Compatibility.Tests.csproj", build)
        for marker in (
            "ExpectedSha256", "ExpectedMvid", "PieceTable", "UpdateAvailable", "ZNetScene", "OnDestroy",
            "SetPlaceMode", "GetBuildTool", "UpdateAvailablePiecesList", "HaveRequirements", "UpdatePlacement",
            "UpdateWalking", "CheckRun", "GetRunSpeedFactor", "ModifyRunStaminaDrain", "UseStamina",
            "CurrentGameVersion", "GetPaintMask", "m_character",
            "m_localPlayer", "m_pieces", "m_craftingStation", "m_resources", "m_resItem", "m_amount",
            "m_paintType", "GetComponent", "m_paintMaskDirt", "m_paintMaskCultivated", "m_paintMaskPaved", "PaintType",
        ):
            self.assertIn(marker, compatibility_test)
        for marker in (
            "RequireMethod", "RequireMethodNamedReturn", "RequirePatchMethod", "RequireField",
            "RequireProperty", "RequireGenericMethod", "RequireEnumValue", "RequireColor",
        ):
            self.assertIn(marker, gate + module)
        self.assertNotIn("ExpectedSha256", gate)
        self.assertNotIn("ExpectedMvid", gate)

    def test_plugin_identity_and_build_commit_are_generated(self):
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text()
        project = PLUGIN_PROJECT.read_text()
        self.assertIn(f'PluginGuid = "{META["plugin_guid"]}"', plugin)
        self.assertIn("PluginVersion = GeneratedBuildInfo.Version", plugin)
        self.assertIn("GeneratedBuildInfo.Commit", plugin)
        self.assertIn("$(SourceRevisionId)", project)

    def test_release_pushes_and_verifies_before_packaging(self):
        script = (ROOT / "scripts/release.sh").read_text()
        push = script.index('git push origin "HEAD:refs/heads/$branch"')
        verify = script.index("git ls-remote", push)
        package = script.index("./scripts/package.py --channel final", verify)
        self.assertLess(push, verify)
        self.assertLess(verify, package)
        self.assertNotRegex(script, r"\bgit\s+tag\b|\bgh\s+release\b|thunderstore\s+upload")

    def test_package_is_exact_allowlist(self):
        script = (ROOT / "scripts/package.py").read_text()
        for name in ("manifest.json", "icon.png", "README.md", "CHANGELOG.md", f"plugins/{{identifier}}/{{identifier}}.dll"):
            self.assertIn(name, script)
        self.assertIn("unexpected ZIP attributes", script)
        self.assertIn("origin/", script)

    def test_original_icon_is_valid_and_pinned(self):
        import hashlib
        icon = ROOT / "packages" / IDENTIFIER / "icon.png"
        renderer = ROOT / "scripts" / "render_icon.py"
        self.assertTrue(icon.is_file())
        self.assertTrue(renderer.is_file())
        data = icon.read_bytes()
        self.assertEqual(b"\x89PNG\r\n\x1a\n", data[:8])
        self.assertEqual((256).to_bytes(4, "big") * 2, data[16:24])
        self.assertEqual("c8efe7f00f42aff1066340bd6d3993df43cf686c3e664632cbc7501301343fee", hashlib.sha256(data).hexdigest())
        self.assertIn("boot", renderer.read_text())

    def test_release_output_contains_only_plugin_and_symbols(self):
        output = PLUGIN_DIR / "bin" / "Release"
        files = sorted(path.name for path in output.iterdir() if path.is_file())
        self.assertEqual([f"{IDENTIFIER}.dll", f"{IDENTIFIER}.pdb"], files)

    def test_no_template_placeholders_remain(self):
        tracked = subprocess.check_output(["git", "ls-files"], cwd=ROOT, text=True).splitlines()
        for relative in tracked:
            path = ROOT / relative
            if path.is_file() and path.suffix.lower() not in {".png"}:
                self.assertNotIn("__" + "IDENTIFIER__", path.read_text(errors="ignore"), relative)
                self.assertNotIn("__" + "SLUG__", path.read_text(errors="ignore"), relative)

    def test_provenance_and_sync_are_reviewable(self):
        provenance = json.loads((ROOT / "template-provenance.json").read_text())
        self.assertRegex(provenance["template_revision"], r"^[0-9a-f]{40}$")
        sync = (ROOT / "scripts/sync-template.sh").read_text()
        self.assertIn("sync-mod.sh", sync)
        self.assertIn("VALHEIM_MOD_TEMPLATE_PATH", sync)


if __name__ == "__main__":
    unittest.main()
