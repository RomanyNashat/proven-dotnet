#!/usr/bin/env bash
# Tests the /handover helper scripts on throwaway repos and fake Claude session transcripts.
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
SCRIPTS="$ROOT/.claude/skills/handover/scripts"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail() { echo "::error::$1"; exit 1; }
check() { grep -qF -- "$2" <<<"$1" || fail "expected '$2' in output of $3"; }
absent() { ! grep -qF -- "$2" <<<"$1" || fail "did NOT expect '$2' in output of $3"; }

mk() {   # mk <dir> <commit date>
  mkdir -p "$T/work/$1"
  git -C "$T/work/$1" init -q -b master
  git -C "$T/work/$1" config user.email "me@corp.test"; git -C "$T/work/$1" config user.name "Me"
  echo x > "$T/work/$1/a"; git -C "$T/work/$1" add a
  GIT_AUTHOR_DATE="$2" GIT_COMMITTER_DATE="$2" git -C "$T/work/$1" commit -qm "init"
}
mk payment-service "2026-07-10T10:00:00"
mk catalog-api       "2026-03-01T10:00:00"
mk Platform/orders-pg "2026-09-01T10:00:00"
git -C "$T/work/payment-service" checkout -qb feat/PAY-12-refunds
echo y > "$T/work/payment-service/b"; git -C "$T/work/payment-service" add b
git -C "$T/work/payment-service" commit -qm "feat(payments): refund validator"
echo dirty > "$T/work/payment-service/c"
git -C "$T/work/catalog-api" -c user.email=other@corp.test -c user.name=Other commit -q --allow-empty -m "someone else"

echo "== discover_repos.py"
out="$(python3 "$SCRIPTS/discover_repos.py" "$T/work" --since 2026-06-01)"; echo "$out"
check  "$out" "with your commits since 2026-06-01: 2" discover
check  "$out" "payment-service" discover
check  "$out" "orders-pg" discover
check  "$out" "1 other repo(s) with no commits from you" discover
absent "$out" "catalog-api" discover
json="$(python3 "$SCRIPTS/discover_repos.py" "$T/work" --since 2026-06-01 --json)"
python3 - "$json" <<'PY'
import json, sys
d = json.loads(sys.argv[1]); p = next(r for r in d["mine"] if r["name"] == "payment-service")
assert p["my_commits"] == 2 and p["uncommitted_files"] == 1 and p["branch"] == "feat/PAY-12-refunds", p
PY

P="$T/projects"; mkdir -p "$P/a" "$P/b"
L="$T/work"
cat > "$P/a/s1.jsonl" <<J
{"type":"user","cwd":"$L/payment-service","timestamp":"2026-09-10T08:00:00Z","message":{"role":"user","content":"<system-reminder>ignore me</system-reminder>Add refund validation for PAY-12, patient 1234567890 test@x.com"}}
{"type":"assistant","cwd":"$L/payment-service","timestamp":"2026-09-10T08:01:00Z","message":{"role":"assistant","content":[{"type":"text","text":"Decided idempotency key instead of dedupe table."},{"type":"tool_use","name":"Edit","input":{"file_path":"$L/payment-service/src/Refund.cs"}}]}}
{"type":"user","cwd":"$L/payment-service","timestamp":"2026-09-10T08:02:00Z","message":{"role":"user","content":[{"type":"tool_result","content":"HUGE OUTPUT"}]}}
{"type":"assistant","isSidechain":true,"cwd":"$L/payment-service","timestamp":"2026-09-10T08:03:00Z","message":{"role":"assistant","content":[{"type":"text","text":"subagent noise"}]}}
J
cat > "$P/b/s2.jsonl" <<J
{"type":"user","cwd":"$L","timestamp":"2026-09-12T08:00:00Z","message":{"role":"user","content":"check both services"}}
{"type":"assistant","cwd":"$L","timestamp":"2026-09-12T08:01:00Z","message":{"role":"assistant","content":[{"type":"text","text":"Reading payment-service config."},{"type":"tool_use","name":"Read","input":{"file_path":"$L/payment-service/appsettings.json"}}]}}
J
cat > "$P/b/s3.jsonl" <<J
{"type":"user","cwd":"/elsewhere/api","timestamp":"2026-09-12T08:00:00Z","message":{"role":"user","content":"unrelated payment-service mention"}}
J

echo "== extract_sessions.py"
out="$(python3 "$SCRIPTS/extract_sessions.py" --repo "$L/payment-service" --since 2026-06-01 --projects-dir "$P")"; echo "$out"
check  "$out" "2 found" extract
check  "$out" "Files edited: src/Refund.cs" extract
check  "$out" "Decided idempotency key" extract
check  "$out" "from a parent folder" extract
check  "$out" "[number]" extract
check  "$out" "[email]" extract
absent "$out" "1234567890" extract
absent "$out" "test@x.com" extract
absent "$out" "HUGE OUTPUT" extract
absent "$out" "subagent noise" extract
absent "$out" "ignore me" extract
absent "$out" "unrelated payment-service mention" extract
out="$(python3 "$SCRIPTS/extract_sessions.py" --repo "$L/catalog-api" --since 2026-06-01 --projects-dir "$P")"
check "$out" "0 found" "extract (catalog-api)"
echo "All handover script checks passed."
