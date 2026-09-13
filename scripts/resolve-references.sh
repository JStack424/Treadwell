#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

candidates=()
[[ -n "${VALHEIM_REFERENCE_PATH:-}" ]] && candidates+=("$VALHEIM_REFERENCE_PATH")
candidates+=(
  "$repo_root/lib/local/ValheimReferences"
  "$HOME/workspace/valheim-references/current"
  "$HOME/workspace/valheim-qol-mods/lib/local/StackmasterReferences"
)

for candidate in "${candidates[@]}"; do
  if [[ -d "$candidate" ]]; then
    exec python3 "$repo_root/scripts/verify_references.py" "$candidate"
  fi
done

printf 'No private Valheim reference directory found. Set VALHEIM_REFERENCE_PATH or populate lib/local/ValheimReferences.\n' >&2
exit 1
