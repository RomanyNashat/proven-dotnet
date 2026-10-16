#!/usr/bin/env bash
# Grades one fixed copy, inside the .NET SDK image on Ubuntu 24.04 (its ICU gives ar-SA a Hijri calendar).
#   grade-one.sh <folder holding ClinicReminders/> <name> <results folder>
# Writes <results>/<name>/: audit.txt (pass/fail), build.txt (pass/fail), main.trx, notzdata.trx, logs.
set -uo pipefail
src=$1; name=$2; out=$3/$name
here="$(cd "$(dirname "$0")/.." && pwd)"
work=/tmp/eval/$name
rm -rf "$work" && mkdir -p "$work" "$out"
cp -r "$src/ClinicReminders" "$work/ClinicReminders"
cp -r "$here/HiddenTests" "$work/HiddenTests"
rm -rf "$work"/*/bin "$work"/*/obj

# H11: no known-vulnerable package, direct or transitive.
if dotnet restore "$work/ClinicReminders/ClinicReminders.csproj" -p:NuGetAudit=true -p:NuGetAuditMode=all \
     -p:NuGetAuditLevel=low '-p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904' >"$out/audit.log" 2>&1; then
  echo pass >"$out/audit.txt"
else
  echo fail >"$out/audit.txt"
fi

if dotnet build "$work/HiddenTests" -c Release >"$out/build.log" 2>&1; then
  echo pass >"$out/build.txt"
else
  echo fail >"$out/build.txt"; exit 0
fi

export FIXTURE_DIR="$work/ClinicReminders"
dotnet test "$work/HiddenTests" -c Release --no-build --filter 'Kind!=NoTzdata' \
  --logger "trx;LogFileName=main.trx" --results-directory "$out" >"$out/test-main.log" 2>&1
mkdir -p /tmp/empty-tz
TZDIR=/tmp/empty-tz dotnet test "$work/HiddenTests" -c Release --no-build --filter 'Kind=NoTzdata' \
  --logger "trx;LogFileName=notzdata.trx" --results-directory "$out" >"$out/test-notzdata.log" 2>&1
exit 0
