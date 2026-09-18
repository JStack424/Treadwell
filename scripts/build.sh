#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

./scripts/render_metadata.py --check
reference_path="$(./scripts/resolve-references.sh)"
identifier="$(python3 -c 'import json; print(json.load(open("mod.json"))["identifier"])')"
code_revision="$(tr -d '\r\n' < RELEASE_CODE_REVISION)"
if [[ ! "$code_revision" =~ ^[0-9a-f]{40}$ ]] || ! git cat-file -e "$code_revision^{commit}"; then
  printf 'RELEASE_CODE_REVISION is not a valid local commit.\n' >&2
  exit 1
fi

./scripts/dotnet.sh restore "$identifier.sln" --locked-mode -p:ValheimReferencePath="$reference_path" -p:SourceRevisionId="$code_revision"
./scripts/dotnet.sh build "$identifier.sln" --configuration Release --no-restore -p:ValheimReferencePath="$reference_path" -p:SourceRevisionId="$code_revision"
./scripts/dotnet.sh run --project "tests/$identifier.Tests/$identifier.Tests.csproj" --configuration Release --no-build
./scripts/dotnet.sh run --project "tests/$identifier.Compatibility.Tests/$identifier.Compatibility.Tests.csproj" --configuration Release --no-build -- "$reference_path/assembly_valheim.dll"
python3 -m unittest discover -s tests/infrastructure -p 'test_*.py' -v
