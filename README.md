# Treadwell

Build roads worth taking.

Generated from JStack424's infrastructure-first Valheim mod template. The scaffold is deployable but intentionally changes no gameplay.

## Projects

- `src/Treadwell/`: BepInEx 5 plugin targeting .NET Framework 4.8.
- `src/Treadwell.Core/`: pure `netstandard2.0` logic, linked into the plugin DLL.
- `tests/Treadwell.Tests/`: zero-framework `net8.0` behavior tests.
- `tests/infrastructure/`: repository, metadata, release, and packaging safety checks.

Each mod owns its source. There is no shared runtime dependency between generated mods.

## Private Valheim references

Private game/runtime DLLs are never committed or packaged. `scripts/resolve-references.sh` checks, in order:

1. `VALHEIM_REFERENCE_PATH`
2. ignored `lib/local/ValheimReferences`
3. `~/workspace/valheim-references/current`
4. the existing ignored Stackmaster reference bundle (migration fallback)

The required filenames live in `scripts/required-references.txt`. The build validates all of them and every reference uses `<Private>false>`.

## Commands

```bash
./scripts/build.sh
./scripts/test-package.sh
./scripts/package.py --verify-only artifacts/test/JStack424-Treadwell-0.1.0-test.zip
```

`build.sh` performs locked restore, warning-as-error Release compilation, core tests, metadata checks, repository audits, and infrastructure tests.

`test-package.sh` creates a local test ZIP without requiring a clean tree or a remote push. It never uploads anything.

For a public candidate:

```bash
# Commit behavior/docs first and configure the GitHub repository as origin.
./scripts/release.sh 0.1.0
```

The release script refuses a dirty starting tree, updates `mod.json`, regenerates derived metadata, runs all checks, commits the version change when needed, requires a GitHub `origin`, pushes and verifies the exact remote commit, and only then creates `artifacts/release/JStack424-Treadwell-0.1.0.zip`. It never uploads to Thunderstore, creates a Git tag, or creates a GitHub Release.

## Metadata

`mod.json` is the only hand-edited source of package identity and version. Run:

```bash
./scripts/render_metadata.py          # regenerate derived files
./scripts/render_metadata.py --check  # fail if derived files drifted
```

README and changelog prose are normal project-owned documents. The initializer creates them once; template sync never overwrites them.

## Feature modules and compatibility

A feature module owns its config entry, compatibility checks, Harmony instance, enable path, and cleanup. Add every reflected game member a feature depends upon to that feature's `ValidateCompatibility` implementation before adding patches. The global gate validates exact runtime versions, `assembly_valheim` MVID, and SHA-256 before enabling any module.

## Template updates

Template sync is deliberately reviewable and tooling-only:

```bash
./scripts/sync-template.sh                  # dry-run and write an approval plan
./scripts/sync-template.sh --apply <revision-from-dry-run>
git diff                                    # review, test, then commit yourself
```

Apply refuses a dirty mod repository or a stale/missing dry-run plan. It updates only the allowlisted generic scripts plus provenance. It never edits `src/`, project docs, package docs, changelogs, metadata, or behavior tests.
