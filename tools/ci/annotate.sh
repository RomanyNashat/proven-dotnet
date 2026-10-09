#!/usr/bin/env bash
# Runs a command. If it fails, the most telling lines of its output become a GitHub error annotation,
# so the failure is readable on the PR page without opening or downloading the raw log.
#   usage: [ANNOTATE_TIMEOUT=<seconds>] bash tools/ci/annotate.sh <command> [args...]
#
# Output goes to a FILE, not a pipe: a stray child process that keeps a pipe open would otherwise
# keep this script waiting long after the command itself has exited. `tail --pid` streams the file
# live and stops when the command exits. ANNOTATE_TIMEOUT kills a command that runs too long, and
# the process list at that moment is added to the log so the culprit is named.
set -uo pipefail
log="$(mktemp)"
if [ -n "${ANNOTATE_TIMEOUT:-}" ]; then
  timeout -k 20 "$ANNOTATE_TIMEOUT" "$@" >"$log" 2>&1 &
else
  "$@" >"$log" 2>&1 &
fi
pid=$!
tail -n +1 -f --pid="$pid" "$log"
wait "$pid"; rc=$?
if [ "$rc" -eq 124 ] || [ "$rc" -eq 137 ]; then
  { echo "error: timed out after ${ANNOTATE_TIMEOUT}s. Processes still running:"; ps -eo pid,ppid,etime,args | grep -E 'dotnet|testhost|BuildHost|MSBuild|sleep' | grep -v grep | cut -c1-200 | tail -20; } | tee -a "$log"
fi
# On success, surface the test totals as a notice, so "passed" can be told apart from "ran nothing".
if [ "$rc" -eq 0 ]; then
  summary="$(sed 's/\x1b\[[0-9;]*m//g' "$log" | grep -E 'Passed!|Total tests|^\s*(Passed|Failed|Skipped): [0-9]+' | tail -5)"
  [ -n "$summary" ] && echo "::notice title=$(basename -- "$1") $(basename -- "${2:-}") results::$(printf '%s' "$summary" | awk '{printf "%s%%0A", $0}')"
fi
if [ "$rc" -ne 0 ]; then
  clean="$(sed 's/\x1b\[[0-9;]*m//g' "$log")"
  # dotnet test: each failed test's name and the lines under it (message, assert), before anything else.
  # Passing tests whose names contain "Exception" or "Failed" would otherwise push them out of the window.
  lines="$(awk '/^ *Failed [A-Za-z_][A-Za-z0-9_.]*/ {n=6} n>0 {print; n--}' <<<"$clean" | grep -v '^ *Stack Trace' | head -40)"
  [ -n "$lines" ] || lines="$(grep -E '✗|[Ee]rror|FAIL|[Ff]ailed|Exception|expected|timed out|[0-9]+ +[0-9]+ +[0-9:-]+ ' <<<"$clean" | grep -v '^ *Passed ' | tail -30)"
  [ -n "$lines" ] || lines="$(tail -25 <<<"$clean")"
  esc="$(printf '%s' "$lines" | sed 's/%/%25/g' | awk '{printf "%s%%0A", $0}')"
  echo "::error title=$(basename -- "$1") $(basename -- "${2:-}") failed (exit $rc)::${esc}"
fi
exit "$rc"
