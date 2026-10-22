"""Can upgrading the parent fix a vulnerable transitive package, or does it need a direct pin?

    dotnet nuget why <project> <package>                  # which parent brings it in
    python parent_fix.py <parent> <package> --current <parent version you have> [--framework net10.0]
                         [--feed URL] [--vulnerability-feed URL] [--prerelease] [--json]

For every published version of the parent, reads which version of the package it asks for on your
target framework. Restore takes the lowest version a range allows, so that lower bound is the version
you get. Each one is checked against the feed's advisories, and the script prints:
  - the parent's newest version and the package version it brings;
  - the lowest parent version, at or above yours and after the last one that brings a vulnerable
    version, that brings a safe one: upgrade to that (never a downgrade);
  - or that no parent version does yet. Then a direct reference to the fixed package (see first_fixed.py)
    is the clear technical need, and its comment says why the parent can't fix it.
Exit code 0 when a parent upgrade fixes it, 1 when only a direct pin does, 2 on error.
Standard library only; shares the version and advisory code with first_fixed.py.
"""
import argparse
import json
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import first_fixed as ff  # noqa: E402


def framework_key(tfm):
    """('net', (10, 0)), ('netstandard', (2, 0)), ('netframework', (4, 6, 2)) or None for 'any'."""
    if not tfm:
        return None
    t = tfm.lower().lstrip(".")
    for prefix, name in (("netstandard", "netstandard"), ("netframework", "netframework"), ("netcoreapp", "net"), ("net", "net")):
        if t.startswith(prefix):
            numbers = re.findall(r"\d+", t[len(prefix):])
            if name == "net" and prefix == "net" and len(numbers) == 1 and len(numbers[0]) >= 2:
                return ("netframework", tuple(int(c) for c in numbers[0]))   # net472 is .NET Framework
            return (name, tuple(int(n) for n in numbers))
    return None


def pick_group(groups, target):
    """The dependency group NuGet would use for `target`: the highest net version at or below it, else the
    highest .NET Standard, else the group with no framework."""
    want = framework_key(target)
    best = None
    for group in groups:
        key = framework_key(group.get("targetFramework"))
        if key and key[0] == "net" and want[0] == "net" and key[1] <= want[1]:
            if best is None or best[0] != "net" or key[1] > best[1]:
                best = (key[0], key[1], group)
    if best:
        return best[2]
    standard = [(framework_key(g.get("targetFramework"))[1], g) for g in groups
                if framework_key(g.get("targetFramework")) and framework_key(g.get("targetFramework"))[0] == "netstandard"]
    if standard and want[0] == "net":
        return max(standard, key=lambda pair: pair[0])[1]
    return next((g for g in groups if not g.get("targetFramework")), None)


def brings(entry, package, target):
    """The package version this parent version restores to on `target`, or None if it doesn't bring it."""
    group = pick_group(entry.get("dependencyGroups") or [], target)
    for dep in (group or {}).get("dependencies") or []:
        if dep["id"].lower() == package.lower():
            low = ff.parse_range(dep.get("range") or "0.0.0")[0]
            text = (dep.get("range") or "").strip("[( ").split(",")[0].strip()
            return text if low is not None else None
    return None


def report(entries, package, advisories, target, prerelease=False, current=None):
    """(newest row, the lowest parent version at or above `current` and after the last vulnerable one that
    brings a safe package, rows)."""
    rows = []
    for entry in entries:
        version = entry["version"]
        if ff.is_prerelease(version) and not prerelease:
            continue
        child = brings(entry, package, target)
        rows.append({"parent": version, "brings": child,
                     "advisories": ff.advisories_for(child, advisories) if child else []})
    rows.sort(key=lambda r: ff.parse_version(r["parent"]))
    last_bad = max((i for i, r in enumerate(rows) if r["advisories"]), default=-1)
    floor = ff.parse_version(current) if current else None
    safe = next((r for r in rows[last_bad + 1:]
                 if r["brings"] and (floor is None or ff.parse_version(r["parent"]) >= floor)), None)
    return (rows[-1] if rows else None), safe, rows


def registration_entries(feed_index, parent):
    base = ff.resource(feed_index, "RegistrationsBaseUrl/3.6.0") or ff.resource(feed_index, "RegistrationsBaseUrl")
    if base is None:
        raise SystemExit("the feed has no registration resource")
    data = ff.fetch_json(f"{base.rstrip('/')}/{parent.lower()}/index.json")
    entries = []
    for page in data["items"]:
        items = page.get("items") or ff.fetch_json(page["@id"])["items"]
        entries += [item["catalogEntry"] for item in items]
    return entries


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("parent")
    parser.add_argument("package")
    parser.add_argument("--current", help="the parent version your project has now")
    parser.add_argument("--framework", default="net10.0", help="your project's target framework")
    parser.add_argument("--feed", default=ff.NUGET_ORG)
    parser.add_argument("--vulnerability-feed")
    parser.add_argument("--prerelease", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    try:
        feed = ff.fetch_json(args.feed)
        vulnerability_feed = ff.fetch_json(args.vulnerability_feed) if args.vulnerability_feed else feed
        vulnerability_index = ff.resource(vulnerability_feed, "VulnerabilityInfo")
        if vulnerability_index is None:
            print(f"This feed publishes no vulnerability data: pass --vulnerability-feed {ff.NUGET_ORG}", file=sys.stderr)
            return 2
        entries = registration_entries(feed, args.parent)
        advisories = ff.known_advisories(vulnerability_index, args.package)
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    newest, safe, rows = report(entries, args.package, advisories, args.framework, args.prerelease, args.current)
    if args.json:
        print(json.dumps({"newest": newest, "safe": safe}, indent=2))
    elif newest is None:
        print(f"{args.parent}: no published version")
        return 2
    else:
        brought = newest["brings"] or "nothing (it doesn't bring the package on this framework)"
        print(f"{args.parent} {newest['parent']} (newest) brings {args.package} {brought} on {args.framework}"
              + (f": {'; '.join(newest['advisories'])}" if newest["advisories"] else ": no known advisory"))
        if safe and args.current and ff.parse_version(safe["parent"]) == ff.parse_version(args.current):
            print(f"  your {args.parent} {args.current} already brings a safe {args.package} ({safe['brings']}):"
                  " if restore still flags it, it comes through another parent (dotnet nuget why lists every path)")
        elif safe:
            print(f"  upgrade {args.parent} to {safe['parent']}: it brings {args.package} {safe['brings']}, no known advisory")
        else:
            print(f"  no version of {args.parent} brings a safe {args.package} yet: pin {args.package} directly"
                  f" (first_fixed.py names the version), with a comment saying the parent can't fix it")
    return 0 if safe else 1


if __name__ == "__main__":
    sys.exit(main())
