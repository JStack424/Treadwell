#!/usr/bin/env bash
set -euo pipefail

pinned_version="8.0.425"
default_root="$HOME/workspace/toolchains/dotnet-8"
dotnet_root="${DOTNET_ROOT:-$default_root}"
dotnet_bin="$dotnet_root/dotnet"

if [[ ! -x "$dotnet_bin" ]]; then
  system_dotnet="$(command -v dotnet || true)"
  if [[ -n "$system_dotnet" ]] && [[ "$("$system_dotnet" --version)" == "$pinned_version" ]]; then
    dotnet_bin="$system_dotnet"
    dotnet_root="$(dirname "$(readlink -f "$system_dotnet")")"
  else
    printf 'Pinned .NET SDK %s not found. Set DOTNET_ROOT or install it at %s.\n' "$pinned_version" "$default_root" >&2
    exit 1
  fi
fi

export DOTNET_ROOT="$dotnet_root"
export PATH="$(dirname "$dotnet_bin"):$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME/workspace/.dotnet-cli-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/workspace/.nuget/packages}"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

actual="$("$dotnet_bin" --version)"
if [[ "$actual" != "$pinned_version" ]]; then
  printf 'Expected .NET SDK %s, found %s.\n' "$pinned_version" "$actual" >&2
  exit 1
fi
exec "$dotnet_bin" "$@"
