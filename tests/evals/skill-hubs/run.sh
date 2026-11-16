#!/usr/bin/env bash
# One arm of the hub eval: every request in requests.tsv, one headless session each, in a fresh copy of
# that arm's listing, with only the project's settings (so the installed skills don't mix in).
#   bash run.sh flat|hubs <runs folder>
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ARM="$1"; OUT="$2"
[ "$ARM" = flat ] || [ "$ARM" = hubs ] || { echo "arm is flat or hubs"; exit 2; }
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
python3 "$HERE/build.py" "$WORK" > "$OUT.listing.json"
NOTE="This is a routing check in an empty folder: there is no code to change. Open the skill that fits the request the way you normally would, then stop and reply with that skill's name only."
mkdir -p "$OUT/$ARM"
n=0
while IFS=$'\t' read -r skill request; do
  case "$skill" in "#"*|"") continue;; esac
  n=$((n + 1))
  (cd "$WORK/$ARM" && timeout 300 claude -p "$request" --setting-sources project --output-format stream-json --verbose \
     --max-turns 6 --allowedTools "Skill" "Read" --append-system-prompt "$NOTE" < /dev/null > "$OUT/$ARM/$n.jsonl" 2>/dev/null) || true
  printf '%s\t%s\n' "$n" "$skill" >> "$OUT/$ARM/index.tsv"
done < "$HERE/requests.tsv"
echo "$OUT/$ARM: $n runs"
