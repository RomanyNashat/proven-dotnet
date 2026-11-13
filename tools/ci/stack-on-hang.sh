#!/usr/bin/env bash
# Names where a hung test run is stuck. Started in the background next to `dotnet test`: after <seconds>
# it takes the managed stacks of every test host still running (dotnet-stack) and puts the frames that
# matter, with the processes still alive, into one warning annotation. Logs can't be read from here, so
# the annotation is the only way to see them.
#   usage: bash tools/ci/stack-on-hang.sh <seconds> &
set -uo pipefail
sleep "$1"
pids="$(pgrep -f 'exec --runtimeconfig .*ProvenRoslynMcp.Tests' || true)"
[ -n "$pids" ] || exit 0
out="$(mktemp)"
{
  echo "test host still running after $1s"
  ps -eo pid,ppid,etime,args | grep -E 'dotnet|testhost|BuildHost|MSBuild' | grep -v grep | cut -c1-160
  for pid in $pids; do
    echo "== stacks of $pid"
    "$HOME/.dotnet/tools/dotnet-stack" report -p "$pid" 2>&1 \
      | grep -v -E '^\s*\[Native Frames\]$' \
      | grep -E 'Thread|ProvenRoslynMcp|CodeAnalysis|Diagnostics.Process|StreamReader|Pipe|Task.Wait|WaitAsync|Monitor|SemaphoreSlim|ManualResetEvent' \
      | grep -v -B0 -E '^Thread' | head -150
  done
} > "$out" 2>&1
cat "$out"
echo "::warning title=stack-on-hang::$(head -c 60000 "$out" | sed 's/%/%25/g' | awk '{printf "%s%%0A", $0}')"
