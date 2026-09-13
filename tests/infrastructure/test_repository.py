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


class RepositoryInfrastructureTests(unittest.TestCase):
    def test_metadata_is_single_source_and_rendered_outputs_are_current(self):
        result = subprocess.run(["python3", "scripts/render_metadata.py", "--check"], cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, META["schema_version"])

    def test_framework_and_dependency_boundaries(self):
        plugin = PLUGIN_PROJECT.read_text()
        core = CORE_PROJECT.read_text()
        tests = TEST_PROJECT.read_text()
        self.assertIn("<TargetFramework>net48</TargetFramework>", plugin)
        self.assertIn("<TargetFramework>netstandard2.0</TargetFramework>", core)
        self.assertIn("<TargetFramework>net8.0</TargetFramework>", tests)
        self.assertIn("ProjectReference", tests)
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

    def test_compatibility_gate_is_exact_and_precedes_features(self):
        gate = (PLUGIN_DIR / "CompatibilityGate.cs").read_text()
        plugin = (PLUGIN_DIR / "Plugin.cs").read_text()
        for marker in ("SupportedValheimMvid", "SupportedValheimSha256", "SupportedGameVersion", "SupportedUnityVersion", "SupportedBepInExVersion", "SupportedHarmonyVersion", "SHA256.Create"):
            self.assertIn(marker, gate)
        self.assertLess(plugin.index("CompatibilityGate.Evaluate"), plugin.index("_features.Start"))
        self.assertIn("before any gameplay hooks were installed", plugin)

    def test_feature_modules_own_enable_disable_and_compatibility(self):
        module = (PLUGIN_DIR / "FeatureModule.cs").read_text()
        host = (PLUGIN_DIR / "FeatureHost.cs").read_text()
        for marker in ("ValidateCompatibility", "void Enable()", "void Disable()", "ConfigEntry<bool> Enabled"):
            self.assertIn(marker, module)
        self.assertIn("new Harmony(Plugin.PluginGuid + \".feature.\" + id)", module)
        self.assertIn("_harmony.UnpatchSelf()", module)
        self.assertIn("foreach (var module in _modules.Reverse())", host)

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

    def test_generated_icon_is_valid_shape(self):
        icon = ROOT / "packages" / IDENTIFIER / "icon.png"
        self.assertTrue(icon.is_file())
        data = icon.read_bytes()
        self.assertEqual(b"\x89PNG\r\n\x1a\n", data[:8])
        self.assertEqual((256).to_bytes(4, "big") * 2, data[16:24])

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
