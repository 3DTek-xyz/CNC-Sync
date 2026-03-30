#!/bin/zsh

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_root="/tmp/dotnet10"
dotnet_home="/tmp/dotnet10-home"
dotnet_bin="$dotnet_root/dotnet"
solution_path="$repo_root/CNCSync.sln"
app_project_path="$repo_root/src/CNCSync.App/CNCSync.App.csproj"

if [[ ! -x "$dotnet_bin" ]]; then
  echo "Expected local .NET 10 SDK at $dotnet_bin but it was not found."
  echo "Restore that toolchain first, then rerun this script."
  exit 1
fi

export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"
export DOTNET_CLI_HOME="$dotnet_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1

mkdir -p "$dotnet_home"

echo "Building CNC Sync..."
"$dotnet_bin" build "$solution_path" \
  --no-restore \
  -m:1 \
  -nodeReuse:false \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -v q

echo "Starting CNC Sync..."
exec "$dotnet_bin" run --project "$app_project_path" --no-build
