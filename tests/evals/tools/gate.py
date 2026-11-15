"""The eval gate: a change to the harness is kept only if no case of the fix evals gets worse.

    python3 tools/gate.py scores <graded.json> > scores.json        # per arm, per case: passes out of runs
    python3 tools/gate.py check <baseline scores.json> <new scores.json>
    python3 tools/gate.py noise <scores.json> <arm A> <arm B>       # two arms of the same harness

`grade.py --json` writes each graded copy's results. Copies are named `<arm>-<n>` (`with-proven-2`);
`original` and `reference*` are the fixture's own checks and don't count. `scores` adds up each arm.

`check` compares two of those, arm by arm and case by case (findings and regressions alike), for every arm
both have. It fails when:
  - a case passes in fewer runs than before, as a rate when the run counts differ (13/14 overall is no
    comfort if the case that dropped is the one the change was about, so the total isn't what's compared);
  - an arm has fewer runs than before (fewer runs can't show a case held).
A case that passes in more runs is reported as better. Exit 1 on any failure.

`noise` reads two arms that ran the same harness at the same ref (`proven-a`, `proven-b`): whatever differs
between them is noise. It prints each case whose pass rate differs and the floor, the largest difference.
A later change has to move a case by more than the floor before it is reported as shown.
"""
import json
import re
import sys

COPY = re.compile(r"^(?P<arm>.+)-(?P<n>\d+)$")


def scores(graded):
    arms = {}
    for name, copy in sorted(graded.items()):
        match = COPY.match(name)
        if name == "original" or name.startswith("reference") or not match:
            continue
        arm = arms.setdefault(match["arm"], {"runs": 0, "built": 0, "passes": {}})
        arm["runs"] += 1
        arm["built"] += copy["built"]
        for case, ok in copy["results"].items():
            arm["passes"][case] = arm["passes"].get(case, 0) + bool(ok)
    return {"arms": arms}


def check(baseline, current):
    failures, better = [], []
    for arm, before in sorted(baseline["arms"].items()):
        now = current["arms"].get(arm)
        if now is None:
            continue
        if now["runs"] < before["runs"]:
            failures.append(f"{arm}: {now['runs']} runs, the baseline has {before['runs']}")
            continue
        for case, was in sorted(before["passes"].items()):
            # Compare as rates when the run counts differ, so four runs aren't measured against three.
            is_ = now["passes"].get(case, 0)
            if is_ * before["runs"] < was * now["runs"]:
                failures.append(f"{arm} {case}: passed in {is_}/{now['runs']} runs, was {was}/{before['runs']}")
            elif is_ * before["runs"] > was * now["runs"]:
                better.append(f"{arm} {case}: {was}/{before['runs']} -> {is_}/{now['runs']}")
    return failures, better


def noise(scores_, first, second):
    a, b = scores_["arms"][first], scores_["arms"][second]
    cases = sorted(set(a["passes"]) | set(b["passes"]))
    differ = []
    for case in cases:
        rate_a = a["passes"].get(case, 0) / a["runs"]
        rate_b = b["passes"].get(case, 0) / b["runs"]
        if rate_a != rate_b:
            differ.append((case, rate_a, rate_b))
    floor = max((abs(x - y) for _, x, y in differ), default=0.0)
    return differ, floor


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) == 2 and argv[0] == "scores":
        with open(argv[1], encoding="utf-8") as f:
            print(json.dumps(scores(json.load(f)), indent=2, sort_keys=True))
        return 0
    if len(argv) == 3 and argv[0] == "check":
        with open(argv[1], encoding="utf-8") as f:
            baseline = json.load(f)
        with open(argv[2], encoding="utf-8") as f:
            current = json.load(f)
        failures, better = check(baseline, current)
        for line in better:
            print(f"better: {line}")
        for line in failures:
            print(f"::error title=eval gate::{line}")
        if not failures:
            print("eval gate: no case got worse")
        return 1 if failures else 0
    if len(argv) == 4 and argv[0] == "noise":
        with open(argv[1], encoding="utf-8") as f:
            graded = json.load(f)
        differ, floor = noise(graded, argv[2], argv[3])
        for case, x, y in differ:
            print(f"{case}: {argv[2]} {x:.0%}, {argv[3]} {y:.0%}")
        print(f"noise floor: {floor:.0%} of runs on one case ({len(differ)} of the cases differ)")
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
