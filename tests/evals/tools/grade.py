"""Grades the copies of a fix-eval fixture from what its grade-one.sh wrote, and prints the table (Markdown).

    python3 tools/grade.py <fixture folder> <results folder> [--json scores.json]

The fixture's `fixture.json` names its finding tests, its regression tests, and any test graded from a
file instead of a test result (`{"H11": "audit.txt"}`). Exit 1 if the fixture itself is off: the original
must fail every finding and pass every regression, and every `reference*` copy must pass everything. A
run's own score never fails the job. With --json, writes each copy's results, which the eval gate reads.
"""
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def outcomes(folder, files):
    found = {}
    for trx in folder.glob("*.trx"):
        for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            # "Namespace.Class.H01_Name", or a display name "H01 what it checks": the key is H01 either way.
            key = r.get("testName").split(" ", 1)[0].rsplit(".", 1)[-1].split("_", 1)[0]
            msg = r.find(".//t:Message", NS)
            text = (msg.text or "").strip().splitlines()[0][:450] if msg is not None and msg.text else ""
            found[key] = (r.get("outcome") == "Passed", text)
    for key, name in files.items():
        f = folder / name
        ok = f.exists() and f.read_text().strip() == "pass"
        found[key] = (ok, "" if ok else f"{name} says fail")
    return found


def grade(fixture, results):
    manifest = json.loads((pathlib.Path(fixture) / "fixture.json").read_text(encoding="utf-8"))
    findings, regressions, files = manifest["findings"], manifest["regressions"], manifest.get("files", {})
    copies = sorted((p for p in pathlib.Path(results).iterdir() if p.is_dir()),
                    key=lambda p: (p.name != "original", not p.name.startswith("reference"), p.name))
    table = {}
    for c in copies:
        built = (c / "build.txt").exists() and (c / "build.txt").read_text().strip() == "pass"
        found = outcomes(c, files) if built else {k: v for k, v in outcomes(c, files).items() if k in files}
        table[c.name] = {"built": built, "results": {k: found.get(k, (False, "did not run" if built else "doesn't build"))
                                                     for k in findings + regressions}}
    return manifest, table


def report(manifest, table):
    findings, regressions = manifest["findings"], manifest["regressions"]
    lines = ["| Test | " + " | ".join(table) + " |", "|---|" + "---|" * len(table)]
    for key in findings + regressions:
        lines.append(f"| {key} | " + " | ".join("pass" if c["results"][key][0] else "**fail**" for c in table.values()) + " |")
    lines.append("| **Findings fixed** | " + " | ".join(
        f"{sum(c['results'][k][0] for k in findings)} / {len(findings)}" for c in table.values()) + " |")
    lines.append("| **Regressions kept** | " + " | ".join(
        f"{sum(c['results'][k][0] for k in regressions)} / {len(regressions)}" for c in table.values()) + " |")
    lines.append("| Builds | " + " | ".join("yes" if c["built"] else "**no**" for c in table.values()) + " |")
    lines += ["", "<details><summary>Failure messages</summary>", ""]
    for name, c in table.items():
        lines += [f"- {name} {k}: {msg}" for k, (ok, msg) in c["results"].items() if not ok and msg]
    lines += ["", "</details>"]
    return "\n".join(lines)


def problems(manifest, table):
    found = []
    findings, regressions = manifest["findings"], manifest["regressions"]
    expected = [("original", False)] + [(n, True) for n in table if n.startswith("reference")]
    for name, want in expected:
        if name not in table:
            continue
        c = table[name]
        if not c["built"]:
            found.append(f"{name} doesn't build")
            continue
        for k in findings:
            if c["results"][k][0] != want:
                found.append(f"{name} {k} should {'pass' if want else 'fail'}: {c['results'][k][1]}")
        for k in regressions:
            if not c["results"][k][0]:
                found.append(f"{name} {k} should pass: {c['results'][k][1]}")
    return found


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    out = None
    if "--json" in argv:
        i = argv.index("--json")
        out = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    fixture, results = argv
    manifest, table = grade(fixture, results)
    print(report(manifest, table))
    for name, c in table.items():
        missed = [k for k, (ok, _) in c["results"].items() if not ok]
        score = sum(c["results"][k][0] for k in manifest["findings"])
        kept = sum(c["results"][k][0] for k in manifest["regressions"])
        # GitHub keeps only ten warnings per step, so each copy's misses go in its own notice.
        print(f"::notice title=fix eval: {name}::findings fixed {score}/{len(manifest['findings'])}, regressions kept "
              f"{kept}/{len(manifest['regressions'])}, builds: {'yes' if c['built'] else 'no'}; failed: {', '.join(missed) or 'none'}",
              file=sys.stderr)
    if out:
        pathlib.Path(out).write_text(json.dumps(
            {name: {"built": c["built"], "results": {k: ok for k, (ok, _) in c["results"].items()}} for name, c in table.items()},
            indent=2) + "\n", encoding="utf-8")
    found = problems(manifest, table)
    for p in found:
        print(f"::error title=fix eval fixture::{p}", file=sys.stderr)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
