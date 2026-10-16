"""Reads what grade-one.sh wrote for every copy and prints the table (Markdown) to stdout.
Exit 1 if the fixture itself is off: the original must fail every finding and pass every regression,
the reference must pass everything. A run's own score never fails the job.
    python3 grade.py <results folder>
"""
import pathlib, re, sys, xml.etree.ElementTree as ET

FINDINGS = ["H01", "H02", "H03", "H04", "H05", "H06", "H07", "H08", "H09", "H10", "H11", "H12", "H13", "H14"]
REGRESSIONS = ["R1", "R2", "R3", "R4", "R5"]
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def outcomes(folder):
    found = {}
    for trx in folder.glob("*.trx"):
        for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            name = r.get("testName").rsplit(".", 1)[-1]
            key = name.split("_", 1)[0]
            msg = r.find(".//t:Message", NS)
            found[key] = (r.get("outcome") == "Passed", (msg.text or "").strip().splitlines()[0][:160] if msg is not None and msg.text else "")
    audit = (folder / "audit.txt").read_text().strip() if (folder / "audit.txt").exists() else "fail"
    found["H11"] = (audit == "pass", "" if audit == "pass" else "restore reports a known-vulnerable package")
    return found


def main(root):
    root = pathlib.Path(root)
    copies = sorted(p for p in root.iterdir() if p.is_dir())
    order = sorted(copies, key=lambda p: (p.name != "original", p.name != "reference", p.name))
    rows, problems = [], []
    table = {}
    for c in order:
        built = (c / "build.txt").exists() and (c / "build.txt").read_text().strip() == "pass"
        o = outcomes(c) if built else {"H11": outcomes(c)["H11"]}
        table[c.name] = (built, o)
    print("| Test | " + " | ".join(table) + " |")
    print("|---|" + "---|" * len(table))
    for key in FINDINGS + REGRESSIONS:
        cells = []
        for name, (built, o) in table.items():
            ok, _ = o.get(key, (False, "did not run" if built else "doesn't build"))
            cells.append("pass" if ok else "**fail**")
        print(f"| {key} | " + " | ".join(cells) + " |")
    print("| **Findings fixed** | " + " | ".join(
        f"{sum(o.get(k, (False,))[0] for k in FINDINGS)} / {len(FINDINGS)}" for _, (b, o) in table.items()) + " |")
    print("| **Regressions kept** | " + " | ".join(
        f"{sum(o.get(k, (False,))[0] for k in REGRESSIONS)} / {len(REGRESSIONS)}" for _, (b, o) in table.items()) + " |")
    print("| Builds | " + " | ".join("yes" if b else "**no**" for b, _ in table.values()) + " |")

    print("\n<details><summary>Failure messages</summary>\n")
    for name, (built, o) in table.items():
        for key in FINDINGS + REGRESSIONS:
            ok, msg = o.get(key, (False, ""))
            if not ok and msg:
                print(f"- {name} {key}: {msg}")
    print("\n</details>")

    for name, want_findings, want_regressions in (("original", False, True), ("reference", True, True)):
        if name not in table:
            continue
        built, o = table[name]
        if not built:
            problems.append(f"{name} doesn't build")
            continue
        for k in FINDINGS:
            if o.get(k, (False,))[0] != want_findings:
                problems.append(f"{name} {k} should {'pass' if want_findings else 'fail'}: {o.get(k, (False, 'did not run'))[1]}")
        for k in REGRESSIONS:
            if o.get(k, (False,))[0] != want_regressions:
                problems.append(f"{name} {k} should pass: {o.get(k, (False, 'did not run'))[1]}")
    for p in problems:
        print(f"::error title=fix eval fixture::{p}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
