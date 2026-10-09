#!/usr/bin/env bash
# Self-test for .claude/hooks/worktree-guard.py: denies an applying rename inside a worktree, allows
# everything else.
set -euo pipefail
cd "$(dirname "$0")/../.."
g() { python3 .claude/hooks/worktree-guard.py <<<"$1"; }
fail() { echo "FAIL: $*"; exit 1; }

out="$(g '{"cwd":"/home/dev/repo/.claude/worktrees/agent-1","tool_input":{"name":"OrderSvc","newName":"OrderService","preview":false}}')"
grep -q '"permissionDecision": "deny"' <<<"$out" || fail "applying rename in a worktree must be denied: $out"
out="$(g '{"cwd":"C:\\Users\\dev\\repo\\.claude\\worktrees\\agent-1","tool_input":{"preview":"false"}}')"
grep -q '"deny"' <<<"$out" || fail "Windows path, string false: must be denied: $out"
[ -z "$(g '{"cwd":"/home/dev/repo/.claude/worktrees/agent-1","tool_input":{"preview":true}}')" ] || fail "a preview must be allowed"
[ -z "$(g '{"cwd":"/home/dev/repo/.claude/worktrees/agent-1","tool_input":{}}')" ] || fail "default (preview) must be allowed"
[ -z "$(g '{"cwd":"/home/dev/repo","tool_input":{"preview":false}}')" ] || fail "outside a worktree must be allowed"
[ -z "$(g 'not json')" ] || fail "malformed input must not block"
echo "worktree-guard: all checks passed"
