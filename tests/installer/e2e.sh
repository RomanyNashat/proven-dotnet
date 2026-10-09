#!/usr/bin/env bash
# End to end through the real wrappers: install the fixture layer into a sandbox that already holds the
# user's own files and settings, then uninstall, and check the user's things are exactly as before.
set -uo pipefail
cd "$(dirname "$0")/../.."
sb="$(mktemp -d)/.claude"
mkdir -p "$sb/skills/mine"
echo mine > "$sb/skills/mine/SKILL.md"
echo '{"model": "opus"}' > "$sb/settings.json"
fail() { echo "::error title=installer e2e (bash)::$1"; exit 1; }

PROVEN_SOURCE=tests/installer/fixture/.claude bash install.sh --claude-dir "$sb" || fail "install.sh exited $?"
[ -f "$sb/rules/proven-fixture/core.md" ] || fail "core.md not installed as a rule"
[ -f "$sb/skills/demo/SKILL.md" ] || fail "skill not installed"
grep -q 'hooks/proven-fixture/hello.py' "$sb/settings.json" || fail "hook not merged into settings.json"
grep -q '"opus"' "$sb/settings.json" || fail "the user's model setting was lost"

PROVEN_LAYER_ID=proven-fixture bash uninstall.sh --claude-dir "$sb" || fail "uninstall.sh exited $?"
left="$(cd "$sb" && find . -type f -not -path './.layers/*' | sort | tr '\n' ' ')"
[ "$left" = "./settings.json ./skills/mine/SKILL.md " ] || fail "after uninstall: $left"
python3 -c "import json,sys; d=json.load(open(sys.argv[1])); sys.exit(0 if d=={'model':'opus'} else 1)" "$sb/settings.json" \
  || fail "settings.json after uninstall: $(cat "$sb/settings.json")"
echo "installer e2e (bash): ok"
