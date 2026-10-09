#!/usr/bin/env bash
# Removes proven-dotnet from ~/.claude: only the files and settings it installed.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
py="$(command -v python3 || command -v python || true)"
[ -n "$py" ] || { echo "Python 3 is needed: https://www.python.org/downloads/"; exit 1; }
exec "$py" "$here/tools/layer.py" uninstall "${PROVEN_LAYER_ID:-proven}" "$@"
