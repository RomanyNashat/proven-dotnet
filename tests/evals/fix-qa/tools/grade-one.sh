#!/usr/bin/env bash
# Grades one copy's tests: they must build and pass on the correct service, then each planted bug is put in
# its place in turn, and the bug counts as caught when at least one test fails on that copy.
#   grade-one.sh <folder holding Pharmacy/RefillDesk.Tests/> <name> <results folder>
# Writes <results>/<name>/: build.txt, R1.txt (the tests build), R2.txt (they all pass on the correct
# service), M01.txt..M10.txt (pass = that bug was caught), tests.txt (how many tests ran) and the logs.
set -uo pipefail
src=$1; name=$2; out=$3/$name
here="$(cd "$(dirname "$0")/.." && pwd)"
work=/tmp/eval-qa/$name
rm -rf "$work" && mkdir -p "$work" "$out"
cp -r "$here/Pharmacy/RefillDesk" "$work/RefillDesk"
cp -r "$src/Pharmacy/RefillDesk.Tests" "$work/RefillDesk.Tests"
rm -rf "$work"/*/bin "$work"/*/obj
correct="$work/RefillDesk/Desk.cs.correct"
cp "$work/RefillDesk/Desk.cs" "$correct"
for m in "$here"/mutants/M*/; do echo fail > "$out/$(basename "$m").txt"; done
echo fail > "$out/R2.txt"

if dotnet build "$work/RefillDesk.Tests" -c Release >"$out/build.log" 2>&1; then
  echo pass > "$out/build.txt"; echo pass > "$out/R1.txt"
else
  echo fail > "$out/build.txt"; echo fail > "$out/R1.txt"; exit 0
fi

run() { # $1 = label; exit status of the tests
  timeout 300 dotnet test "$work/RefillDesk.Tests" -c Release --blame-hang-timeout 60s \
    --logger "trx;LogFileName=$1.trx" --results-directory "$out/trx" >"$out/test-$1.log" 2>&1
}
if run correct; then
  echo pass > "$out/R2.txt"
else
  exit 0  # a test that fails on the correct service makes every catch meaningless
fi
grep -oE "Total: +[0-9]+" "$out/test-correct.log" | grep -oE "[0-9]+" | tail -1 > "$out/tests.txt"

for m in "$here"/mutants/M*/; do
  id=$(basename "$m")
  cp "$m/Desk.cs" "$work/RefillDesk/Desk.cs"
  if dotnet build "$work/RefillDesk.Tests" -c Release >"$out/build-$id.log" 2>&1; then
    run "$id" || echo pass > "$out/$id.txt"
  fi
done
cp "$correct" "$work/RefillDesk/Desk.cs"
exit 0
