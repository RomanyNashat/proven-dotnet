#!/usr/bin/env bash
# Self-test for .claude/hooks/journal.py: the raw capture and the distilled journal never keep a
# national ID, a mobile number, an email or a secret; clean text is left alone; the journal stays
# git-ignored.
set -euo pipefail
cd "$(dirname "$0")/../.."
t="$(mktemp -d)"; trap 'rm -rf "$t"' EXIT
fail() { echo "FAIL: $*"; exit 1; }
git -C "$t" init -q

prompt='booking fails for national ID 1023456789, mobile 0551234567, ali@example.com; conn Host=db;Password=Winter2026!; header Authorization: Bearer abcdefghijklmnopqrstuvwx. Ticket APPT-117 on 2026-10-05, port 6379.'
python3 - "$t" "$prompt" <<'PY' | python3 .claude/hooks/journal.py prompt
import json, sys
print(json.dumps({"cwd": sys.argv[1], "prompt": sys.argv[2]}))
PY
raw="$t/.claude/.journal-raw.md"
[ -f "$raw" ] || fail "no raw capture"
for leak in 1023456789 0551234567 ali@example.com Winter2026 abcdefghijklmnopqrstuvwx; do
  grep -q "$leak" "$raw" && fail "raw capture kept $leak"
done
for kept in APPT-117 2026-10-05 6379 '\[number\]' '\[email\]' 'Password=\[secret\]' 'Bearer \[token\]'; do
  grep -q "$kept" "$raw" || fail "raw capture lost '$kept': $(cat "$raw")"
done

# The distilled journal is masked in place on the next reply...
cat > "$t/.claude/journal.md" <<'MD'
## 2026-10-05 — APPT-101
Debugged a booking for national ID 1023456789. Local test used Password=Winter2026!.
MD
echo "{\"cwd\": \"$t\"}" | python3 .claude/hooks/journal.py response
grep -q 1023456789 "$t/.claude/journal.md" && fail "journal kept the national ID"
grep -q Winter2026 "$t/.claude/journal.md" && fail "journal kept the password"
grep -q "## 2026-10-05 — APPT-101" "$t/.claude/journal.md" || fail "journal heading damaged"

# Raw lines written before masking existed are cleaned on the same reply.
printf -- '- **old — asked:** id 1099887766 token=abc123\n' > "$t/.claude/.journal-raw.md.1"
echo "{\"cwd\": \"$t\"}" | python3 .claude/hooks/journal.py response
grep -q 1099887766 "$t/.claude/.journal-raw.md.1" && fail "rotated raw file kept the ID"
grep -q abc123 "$t/.claude/.journal-raw.md.1" && fail "rotated raw file kept the token"

# ...and a clean journal is not rewritten.
printf '## 2026-10-05 — clean\nNothing sensitive, PR #29, v10.41.6.\n' > "$t/.claude/journal.md"
touch -d '2020-01-01' "$t/.claude/journal.md"
before=$(stat -c %Y "$t/.claude/journal.md")
echo "{\"cwd\": \"$t\"}" | python3 .claude/hooks/journal.py response
[ "$(stat -c %Y "$t/.claude/journal.md")" = "$before" ] || fail "clean journal was rewritten"

grep -qx '.claude/journal.md' "$t/.gitignore" || fail "journal not git-ignored"
grep -qx '.claude/.journal-raw.md' "$t/.gitignore" || fail "raw journal not git-ignored"
(cd "$t" && echo 'garbage' | python3 "$OLDPWD/.claude/hooks/journal.py" prompt)   # bad input never breaks the session
echo "journal: all checks passed"
