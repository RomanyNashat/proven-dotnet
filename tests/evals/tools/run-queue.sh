#!/usr/bin/env bash
# Runs a round's runs one after another, each started or resumed until it writes report.md, and stops at
# the first usage limit (printing when it resets) so the next window carries on where this one left off.
#
#   bash tests/evals/tools/run-queue.sh <fixture> <rounds folder> <name>=<arm>@<ref> ...
#     name is the run folder's name; arm with-proven | rules-only; ref the git ref to install
set -uo pipefail
FIXTURE="$1"; ROOT="$2"; shift 2
HERE="$(cd "$(dirname "$0")" && pwd)"
limit() { grep -ho "You've hit your [^\"]*" "$1"/transcript*.jsonl 2>/dev/null | tail -1; }
for spec in "$@"; do
  name="${spec%%=*}"; rest="${spec#*=}"; arm="${rest%%@*}"; ref="${rest#*@}"; run="$ROOT/$name"
  for attempt in 1 2 3; do
    [ -f "$run/work/report.md" ] && { echo "$name: done"; break; }
    if [ ! -f "$run/transcript.jsonl" ]; then bash "$HERE/run-round.sh" "$FIXTURE" "$arm" "$ref" "$run" >/dev/null
    else bash "$HERE/resume-run.sh" "$run" "$arm" >/dev/null; fi
    last="$(ls -t "$run"/transcript*.jsonl | head -1)"
    if grep -q "You've hit your" "$last"; then echo "$name: stopped on the limit: $(limit "$run")"; exit 3; fi
  done
  [ -f "$run/work/report.md" ] || echo "$name: no report.md after 3 tries"
done
echo "queue finished"
