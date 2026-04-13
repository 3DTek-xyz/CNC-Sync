#!/bin/zsh

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_root="/tmp/dotnet10"
dotnet_home="/tmp/dotnet10-home"
dotnet_bin="$dotnet_root/dotnet"
dotnet_install_script="/tmp/dotnet-install.sh"
dotnet_sdk_version="10.0.201"
solution_path="$repo_root/CNCSync.sln"
app_project_path="$repo_root/src/CNCSync.App/CNCSync.App.csproj"
app_binary_path="$repo_root/src/CNCSync.App/bin/Debug/net10.0/CNCSync"
run_log_path="/tmp/cnc-sync-macos-local.log"

restore_local_dotnet() {
  echo "Local .NET 10 SDK was not found at $dotnet_bin."
  echo "Restoring SDK $dotnet_sdk_version into $dotnet_root..."

  mkdir -p "$dotnet_root" "$dotnet_home"

  if [[ ! -f "$dotnet_install_script" ]]; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$dotnet_install_script"
    chmod +x "$dotnet_install_script"
  fi

  "$dotnet_install_script" --version "$dotnet_sdk_version" --install-dir "$dotnet_root"
}

if [[ ! -x "$dotnet_bin" ]]; then
  restore_local_dotnet
fi

export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"
export DOTNET_CLI_HOME="$dotnet_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1

mkdir -p "$dotnet_home"

existing_pids=(
  $(pgrep -f "$app_project_path" || true)
  $(pgrep -f "$app_binary_path" || true)
)

if (( ${#existing_pids[@]} > 0 )); then
  unique_existing_pids=($(printf "%s\n" "${existing_pids[@]}" | awk 'NF && !seen[$0]++'))
  echo "Stopping existing CNC Sync instance(s): ${unique_existing_pids[*]}"
  pkill -f "$app_project_path" || true
  pkill -f "$app_binary_path" || true
  sleep 1
fi

echo "Restoring CNC Sync..."
"$dotnet_bin" restore "$solution_path" \
  -m:1 \
  -p:BuildInParallel=false

echo "Building CNC Sync..."
"$dotnet_bin" build "$solution_path" \
  --no-restore \
  -m:1 \
  -nodeReuse:false \
  -p:UseSharedCompilation=false \
  -p:BuildInParallel=false \
  -v q

if [[ ! -x "$app_binary_path" ]]; then
  echo "Expected built app at $app_binary_path but it was not found."
  exit 1
fi

echo "Starting CNC Sync..."
"$app_binary_path" >"$run_log_path" 2>&1 &
app_pid=$!

echo "CNC Sync started in the background."
echo "PID: $app_pid"
echo "Log: $run_log_path"
