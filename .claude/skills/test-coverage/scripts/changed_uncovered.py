#!/usr/bin/env python3
"""Lists the lines a change added or edited that no test runs: the test gap of a merge request.

    git diff -U0 origin/main...HEAD --output=change.diff
    python changed_uncovered.py change.diff coverage.cobertura.xml [--min 80]

(`--output` lets git write the file: Windows PowerShell's `>` would write UTF-16. Both are read anyway.)

Only lines the coverage report can measure count (a comment or a brace isn't a gap). A changed source
file the report doesn't list at all is reported on its own: no test loaded it. Test files and anything
that isn't C# are left out. With --min, the exit code is 1 when the change's covered share is below it.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET

HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(?P<start>\d+)(?:,(?P<count>\d+))? @@")
TEST_PATH = re.compile(r"(^|/)(tests?|[^/]*\.Tests?)(/|$)", re.IGNORECASE)


def changed_lines(diff_text):
    """{path: set of new-side line numbers} for added or edited lines."""
    changed, path = {}, None
    for line in diff_text.splitlines():
        if line.startswith("+++ "):
            target = line[4:].strip()
            path = None if target == "/dev/null" else re.sub(r"^b/", "", target)
            continue
        match = HUNK.match(line)
        if match and path:
            start, count = int(match["start"]), int(match["count"] or 1)
            changed.setdefault(path, set()).update(range(start, start + count))
    return {p: lines for p, lines in changed.items()
            if p.endswith(".cs") and not TEST_PATH.search(p) and lines}


def coverage(report_path):
    """{normalized filename: {line: (hits, method)}}."""
    files = {}
    for cls in ET.parse(report_path).getroot().iter("class"):
        filename = cls.get("filename", "").replace("\\", "/")
        owner = re.sub(r"/<([^>]+)>[a-z]__\d+$", r".\1", cls.get("name", ""))
        lines = files.setdefault(filename, {})
        for method in cls.iter("method"):
            name = method.get("name", "")
            label = owner if name == "MoveNext" else f"{owner}.{name}"
            for line in method.iter("line"):
                lines[int(line.get("number"))] = (int(line.get("hits", "0")), label)
        for line in cls.findall("./lines/line"):
            lines.setdefault(int(line.get("number")), (int(line.get("hits", "0")), owner))
    return files


def find(files, path):
    return next((lines for name, lines in files.items() if name == path or name.endswith("/" + path)), None)


def analyse(diff_text, report_path):
    files, rows = coverage(report_path), []
    for path, changed in sorted(changed_lines(diff_text).items()):
        measured = find(files, path)
        if measured is None:
            rows.append((path, None, None, {}))
            continue
        coverable = sorted(changed & measured.keys())
        gaps = {}
        for number in coverable:
            hits, method = measured[number]
            if hits == 0:
                gaps.setdefault(method, []).append(number)
        rows.append((path, len(coverable), len(coverable) - sum(map(len, gaps.values())), gaps))
    return rows


def read_text(path):
    data = open(path, "rb").read()
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16")
    return data.decode("utf-8-sig")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("diff")
    parser.add_argument("report")
    parser.add_argument("--min", type=float, default=None, help="fail below this percentage of changed lines covered")
    args = parser.parse_args(argv)

    rows = analyse(read_text(args.diff), args.report)
    total = sum(r[1] or 0 for r in rows)
    covered = sum(r[2] or 0 for r in rows)
    for path, coverable, hit, gaps in rows:
        if coverable is None:
            print(f"{path}: not in the coverage report (no test loads this file)")
        elif coverable == 0:
            print(f"{path}: nothing measurable changed")
        else:
            print(f"{path}: {hit}/{coverable} changed lines covered")
            for method, numbers in sorted(gaps.items()):
                print(f"    {method}: line{'s' if len(numbers) > 1 else ''} {', '.join(map(str, numbers))}")
    share = 100.0 * covered / total if total else 100.0
    print(f"changed lines covered: {covered}/{total} ({share:.0f}%)")
    untested = [r[0] for r in rows if r[1] is None]
    if untested:
        print(f"files no test loads: {len(untested)}")
    if args.min is not None and (share < args.min or untested):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
