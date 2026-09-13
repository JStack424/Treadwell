#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if [[ $# -ne 1 ]] || [[ ! "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  printf 'usage: scripts/release.sh MAJOR.MINOR.PATCH\n' >&2
  exit 2
fi
version="$1"

if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
  printf 'Release requires a clean starting tree. Commit behavior and documentation first.\n' >&2
  exit 1
fi
branch="$(git branch --show-current)"
[[ -n "$branch" ]] || { printf 'Release requires a named git branch.\n' >&2; exit 1; }
origin="$(git remote get-url origin 2>/dev/null || true)"
if [[ ! "$origin" =~ ^(git@github\.com:|https://github\.com/)[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(\.git)?$ ]]; then
  printf 'Release requires origin to be a configured GitHub repository.\n' >&2
  exit 1
fi

python3 - "$version" <<'PY'
import json, pathlib, sys
path = pathlib.Path("mod.json")
data = json.loads(path.read_text())
data["version"] = sys.argv[1]
path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
PY
./scripts/render_metadata.py
if ! grep -Fqx "## $version" CHANGELOG.md; then
  printf 'CHANGELOG.md must contain an exact "## %s" heading before release.\n' "$version" >&2
  git checkout -- mod.json build/Generated.Mod.props packages/*/manifest.json
  exit 1
fi

./scripts/build.sh
if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
  git add mod.json build/Generated.Mod.props packages/*/manifest.json
  git commit -m "Release $version"
fi
if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
  printf 'Release tree became dirty outside the controlled version files.\n' >&2
  exit 1
fi

author="$(python3 -c 'import json; print(json.load(open("mod.json"))["author"])')"
identifier="$(python3 -c 'import json; print(json.load(open("mod.json"))["identifier"])')"
output="artifacts/release/$author-$identifier-$version.zip"
if [[ -e "$output" ]]; then
  printf 'Refusing to replace existing release bundle: %s\n' "$output" >&2
  exit 1
fi

# Joe's standing invariant: the exact source commit reaches GitHub before the bundle exists.
git push origin "HEAD:refs/heads/$branch"
head="$(git rev-parse HEAD)"
remote="$(git ls-remote --heads origin "refs/heads/$branch" | awk 'NR==1 {print $1}')"
if [[ "$remote" != "$head" ]]; then
  printf 'Remote verification failed: origin/%s is not exact local HEAD. No bundle was created.\n' "$branch" >&2
  exit 1
fi

./scripts/package.py --channel final --output "$output"
printf 'Release bundle created after verified GitHub push: %s\n' "$output"
printf 'This script did not upload to Thunderstore, create a tag, or create a GitHub Release.\n'
