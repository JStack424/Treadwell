#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

./scripts/render_metadata.py --check
reference_path="$(./scripts/resolve-references.sh)"
identifier="$(python3 -c 'import json; print(json.load(open("mod.json"))["identifier"])')"
head="$(git rev-parse HEAD)"

./scripts/dotnet.sh restore "$identifier.sln" --locked-mode -p:ValheimReferencePath="$reference_path"
./scripts/dotnet.sh build "$identifier.sln" --configuration Release --no-restore -p:ValheimReferencePath="$reference_path" -p:SourceRevisionId="$head"
./scripts/dotnet.sh run --project "tests/$identifier.Tests/$identifier.Tests.csproj" --configuration Release --no-build
python3 -m unittest discover -s tests/infrastructure -p 'test_*.py' -v
