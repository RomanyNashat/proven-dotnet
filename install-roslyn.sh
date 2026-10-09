#!/usr/bin/env bash
# Build and install the proven-dotnet Roslyn MCP server as a global .NET tool (bash / Linux / macOS / WSL).
# Can be run standalone to rebuild the server without touching the rest of the harness.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="${1:-$SCRIPT_DIR/tools/proven-roslyn-mcp}"

cyan()  { printf '\033[0;36m→ %s\033[0m\n' "$1"; }
green() { printf '\033[0;32m  ✓ %s\033[0m\n' "$1"; }
yellow(){ printf '\033[0;33m  ! %s\033[0m\n' "$1"; }

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK not found. Install the .NET SDK, then re-run." >&2
  exit 1
fi
if [ ! -d "$PROJECT_DIR" ]; then
  echo "Roslyn project not found at $PROJECT_DIR" >&2
  exit 1
fi

cd "$PROJECT_DIR"

cyan "Building Roslyn MCP server"
dotnet build -c Release

cyan "Packing tool"
dotnet pack -c Release -o ./nupkg

cyan "Installing global tool 'proven-roslyn-mcp'"
if dotnet tool list --global | grep -iq 'proven.roslyn.mcp'; then
  dotnet tool update --global --add-source ./nupkg Proven.Roslyn.Mcp
else
  dotnet tool install --global --add-source ./nupkg Proven.Roslyn.Mcp
fi
green "proven-roslyn-mcp installed"

echo ""
# Registration is ONCE, for every project (user scope, stored in ~/.claude.json) - not per project.
if [ -f "$HOME/.claude.json" ] && grep -q '"proven-roslyn"' "$HOME/.claude.json"; then
  green "proven-roslyn is already registered with Claude Code - nothing to do per project. Restart Claude Code."
else
  yellow "One-time step: register it for every project, then restart Claude Code:"
  yellow "  claude mcp add --scope user --transport stdio proven-roslyn proven-roslyn-mcp"
  yellow "(A repo's own .mcp.json is only for overriding settings in that one repo.)"
fi
