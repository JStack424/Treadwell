#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
./scripts/build.sh
if [[ $# -gt 1 ]]; then
  printf 'usage: scripts/test-package.sh [OUTPUT.zip]\n' >&2
  exit 2
fi
if [[ $# -eq 1 ]]; then
  exec ./scripts/package.py --channel test --output "$1"
fi
exec ./scripts/package.py --channel test
