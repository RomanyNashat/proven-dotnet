#!/usr/bin/env bash
# Grades one copy inside the .NET 10 SDK image: the hidden tests run on .NET 10 against it.
#   grade-one.sh <folder holding ClinicQueue/> <name> <results folder>
# Writes <results>/<name>/: build.txt (pass/fail), main.trx and logs. Needs PostgreSQL at $EVAL_PG.
set -uo pipefail
src=$1; name=$2; out=$3/$name
here="$(cd "$(dirname "$0")/.." && pwd)"
work=/tmp/eval/$name
rm -rf "$work" && mkdir -p "$work" "$out"
cp -r "$src/ClinicQueue" "$work/ClinicQueue"
cp -r "$here/HiddenTests" "$work/HiddenTests"
rm -rf "$work"/*/bin "$work"/*/obj

if dotnet build "$work/HiddenTests" -c Release >"$out/build.log" 2>&1; then
  echo pass >"$out/build.txt"
else
  echo fail >"$out/build.txt"; exit 0
fi

dotnet test "$work/HiddenTests" -c Release --no-build --blame-hang-timeout 120s \
  --logger "trx;LogFileName=main.trx" --results-directory "$out" >"$out/test-main.log" 2>&1
exit 0
