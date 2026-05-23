#!/usr/bin/env bash
#
# Kill orphaned Prismedia dev processes (dotnet, node/vite, docs).
# Safe to run when nothing is running — reports "nothing to kill".
#

set -euo pipefail

killed=0

kill_matching() {
  local label="$1"
  shift
  local pids
  pids=$(pgrep -f "$@" 2>/dev/null || true)
  if [[ -n "$pids" ]]; then
    echo "Killing $label (PIDs: $(echo $pids | tr '\n' ' '))"
    echo "$pids" | xargs kill -9 2>/dev/null || true
    killed=$((killed + $(echo "$pids" | wc -l | tr -d ' ')))
  fi
}

# .NET backend (API + Worker)
kill_matching ".NET API"        "Prismedia\.Api"
kill_matching ".NET Worker"     "Prismedia\.Worker"
kill_matching "dotnet watch"    "dotnet watch.*Prismedia"

# Node / frontend
kill_matching "Vite (SvelteKit)" "vite.*web-svelte"
kill_matching "SvelteKit node"   "node.*web-svelte"
kill_matching "turbo"            "turbo.*dev.*prismedia"

# Docs site
kill_matching "Docusaurus"       "docusaurus.*start\|docusaurus.*serve"
kill_matching "Docs node"        "node.*docs-site"

# Ports — catch anything else lingering on dev ports
for port in 8008 8010 3010 5173; do
  port_pid=$(lsof -ti :"$port" 2>/dev/null || true)
  if [[ -n "$port_pid" ]]; then
    echo "Killing process on port $port (PID: $port_pid)"
    echo "$port_pid" | xargs kill -9 2>/dev/null || true
    killed=$((killed + 1))
  fi
done

if [[ "$killed" -eq 0 ]]; then
  echo "Nothing to kill — no orphaned processes found."
else
  echo "Done. Killed $killed process(es)."
fi
