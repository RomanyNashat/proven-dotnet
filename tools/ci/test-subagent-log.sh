#!/usr/bin/env bash
# Self-test for .claude/hooks/subagent-log.py: counts tool calls and Roslyn calls from a transcript,
# survives a missing transcript and bad input, records no content.
set -euo pipefail
cd "$(dirname "$0")/../.."
t="$(mktemp -d)"; trap 'rm -rf "$t"' EXIT
export HOME="$t" USERPROFILE="$t"
fail() { echo "FAIL: $*"; exit 1; }
cat > "$t/transcript.jsonl" <<'JSONL'
{"type":"user","message":{"role":"user","content":"review OrderService for patient 1089234567"}}
{"type":"assistant","message":{"content":[{"type":"text","text":"looking"},{"type":"tool_use","name":"mcp__proven-roslyn__find_callers","input":{"name":"OrderService"}}]}}
{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"/repo/Secret.cs"}},{"type":"tool_use","name":"mcp__proven-roslyn__find_callers","input":{}}]}}
not json
{"type":"assistant","message":{"content":[{"type":"tool_use","name":"mcp__proven-roslyn__find_references","input":{}}]}}
JSONL
echo "{\"agent_type\":\"code-reviewer\",\"agent_transcript_path\":\"$t/transcript.jsonl\"}" | python3 .claude/hooks/subagent-log.py
echo '{"agent_type":"planner"}' | python3 .claude/hooks/subagent-log.py
echo 'garbage' | python3 .claude/hooks/subagent-log.py
log="$t/.claude/proven/agent-runs.log"
[ "$(wc -l < "$log")" -eq 2 ] || fail "expected 2 lines: $(cat "$log")"
python3 - "$log" <<'PY' || exit 1
import json, sys
a, b = [json.loads(l) for l in open(sys.argv[1])]
assert a["agent"] == "code-reviewer" and a["tools"] == 4 and a["roslyn"] == 3, a
assert a["roslyn_tools"] == {"find_callers": 2, "find_references": 1}, a
assert b["agent"] == "planner" and b["tools"] is None, b
raw = open(sys.argv[1]).read()
assert "1089234567" not in raw and "Secret.cs" not in raw and "OrderService" not in raw, "content leaked"
PY
echo "subagent-log: all checks passed"
