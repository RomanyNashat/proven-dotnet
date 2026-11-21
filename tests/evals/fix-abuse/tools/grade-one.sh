#!/usr/bin/env bash
# Grades one copy of the clinic's visit desk with the tests the run never saw.
#   grade-one.sh <folder holding Clinic/VisitDesk/> <name> <results folder>
# Writes <results>/<name>/: build.txt (pass/fail), hidden.trx and the logs.
set -uo pipefail
src=$1; name=$2; out=$3/$name
here="$(cd "$(dirname "$0")/.." && pwd)"
work=/tmp/eval-abuse/$name
rm -rf "$work" && mkdir -p "$work" "$out"
cp -r "$src/Clinic/VisitDesk" "$work/VisitDesk"
cp -r "$here/HiddenTests" "$work/HiddenTests"
rm -rf "$work"/*/bin "$work"/*/obj

if dotnet build "$work/HiddenTests" -c Release -p:FixtureDir="$work/VisitDesk" >"$out/build.log" 2>&1; then
  echo pass >"$out/build.txt"
else
  echo fail >"$out/build.txt"; exit 0
fi
timeout 600 dotnet test "$work/HiddenTests" -c Release --no-build --blame-hang-timeout 120s \
  --logger "trx;LogFileName=hidden.trx" --results-directory "$out" >"$out/test.log" 2>&1
exit 0
