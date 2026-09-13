#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
template_root="${VALHEIM_MOD_TEMPLATE_PATH:-$HOME/workspace/valheim-mod-template}"
if [[ ! -x "$template_root/scripts/sync-mod.sh" ]]; then
  printf 'Template sync tool not found at %s. Set VALHEIM_MOD_TEMPLATE_PATH.\n' "$template_root" >&2
  exit 1
fi
exec "$template_root/scripts/sync-mod.sh" --target "$repo_root" "$@"
