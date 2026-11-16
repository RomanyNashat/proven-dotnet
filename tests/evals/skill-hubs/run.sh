#!/usr/bin/env bash
# One arm of the hub eval: every request in requests.tsv, one headless session each, in a fresh copy of
# that arm's listing, with only the project's settings (so the installed skills don't mix in).
#   bash run.sh flat|hubs <runs folder>
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ARM="$1"; mkdir -p "$2"; OUT="$(cd "$2" && pwd)"
[ "$ARM" = flat ] || [ "$ARM" = hubs ] || { echo "arm is flat or hubs"; exit 2; }
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
python3 "$HERE/build.py" "$WORK" > "$OUT.listing.json"
NOTE="This is a routing check in an empty folder: there is no code to change. Open the skill that fits the request the way you normally would, then stop and reply with that skill's name only."
mkdir -p "$OUT/$ARM"
n=0
while IFS=$'\t' read -r skill request; do
  case "$skill" in "#"*|"") continue;; esac
  n=$((n + 1))
  if [ -n "${RESUME:-}" ] && [ -f "$OUT/$ARM/$n.jsonl" ] && ! grep -q '"api_error"' "$OUT/$ARM/$n.jsonl"; then continue; fi
  (cd "$WORK/$ARM" && timeout 300 claude -p "$request" --setting-sources project --output-format stream-json --verbose \
     --max-turns 6 --allowedTools "Skill" "Read" --append-system-prompt "$NOTE" < /dev/null > "$OUT/$ARM/$n.jsonl" 2>/dev/null) || true
  grep -q "^$n	" "$OUT/$ARM/index.tsv" 2>/dev/null || printf '%s\t%s\n' "$n" "$skill" >> "$OUT/$ARM/index.tsv"
  if grep -q '"usage_limit_reached"' "$OUT/$ARM/$n.jsonl"; then echo "usage limit at run $n: stopping; RESUME=1 picks up from here"; exit 3; fi
done < "${REQUESTS:-$HERE/requests.tsv}"
echo "$OUT/$ARM: $n runs"
