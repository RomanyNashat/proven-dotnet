#!/usr/bin/env bash
# Installs or updates proven-dotnet in ~/.claude. Safe to run again, and it never touches your own
# files or settings: see tools/layer.py. Extra arguments go to layer.py (e.g. --claude-dir DIR).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
py="$(command -v python3 || command -v python || true)"
[ -n "$py" ] || { echo "Python 3 is needed (the hooks use it too): https://www.python.org/downloads/"; exit 1; }
exec "$py" "$here/tools/layer.py" install "${PROVEN_SOURCE:-$here/.claude}" "$@"
