#!/usr/bin/env python3
"""Ranks methods by risk from a Cobertura report: complex code that tests don't reach comes first.

    python coverage_risk.py coverage.cobertura.xml [--top 15]

Risk is the CRAP score: complexity^2 * (1 - coverage)^3 + complexity. Coverage is the method's branch
rate when it has branches, its line rate otherwise. A simple method scores its complexity whatever its
coverage; a complex, untested one scores the square. Above 30 is worth a test before the next change.

Coverlet reports an async method as its state machine (`Type/<GetAsync>d__3`, method `MoveNext`);
those rows are folded back into `Type.GetAsync`.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET

STATE_MACHINE = re.compile(r"^(?P<type>[^/]+)/<(?P<method>[^>]+)>[a-z]__\d+$")
THRESHOLD = 30


def methods(path):
    root = ET.parse(path).getroot()
    found = {}
    for cls in root.iter("class"):
        class_name = cls.get("name", "")
        filename = cls.get("filename", "")
        for method in cls.iter("method"):
            name = method.get("name", "")
            match = STATE_MACHINE.match(class_name)
            if match:
                owner, name = match["type"], match["method"]
            else:
                owner = class_name
            if name.startswith(".") or name.startswith("<"):
                continue  # constructors and compiler-generated lambdas: their lines count in the owner
            complexity = float(method.get("complexity", "1") or 1)
            lines = [line for line in method.iter("line")]
            branches = [line for line in lines if line.get("branch") == "True" or line.get("branch") == "true"]
            coverage = float(method.get("branch-rate" if branches else "line-rate", "0") or 0)
            key = f"{owner}.{name}"
            # A method reported more than once (an iterator and its state machine) keeps the worst row.
            row = (key, filename, complexity, coverage)
            if key not in found or crap(complexity, coverage) > crap(found[key][2], found[key][3]):
                found[key] = row
    return list(found.values())


def crap(complexity, coverage):
    return complexity ** 2 * (1 - coverage) ** 3 + complexity


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("report")
    parser.add_argument("--top", type=int, default=15)
    args = parser.parse_args(argv)

    ranked = sorted(methods(args.report), key=lambda m: crap(m[2], m[3]), reverse=True)
    print(f"{'CRAP':>6}  {'cx':>3}  {'cov':>4}  method")
    for key, filename, complexity, coverage in ranked[: args.top]:
        flag = "  <- test before changing" if crap(complexity, coverage) > THRESHOLD else ""
        print(f"{crap(complexity, coverage):6.1f}  {complexity:3.0f}  {coverage:4.0%}  {key}  ({filename}){flag}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
