"""The first version of a NuGet package that no known advisory covers, before you pin it.

    python first_fixed.py <package> <current version> [--feed URL] [--prerelease] [--json]

Reads the advisories from the feed's vulnerability data (nuget.org's by default, the same data `dotnet
restore` audits against) and the published versions from the feed. Prints the advisories the current
version is in, then two answers:
  - same line: the lowest safe version with the current major.minor (the smallest change), if there is one;
  - newer: the lowest safe version above the current one, on any line.
Exit code 0 when a safe version was found, 1 when none exists yet (a risk decision, not a pin), 2 on error.

Standard library only. A private feed that doesn't publish vulnerability data (a proxy such as Nexus may
not) still answers for versions; pass --vulnerability-feed to read the advisories from nuget.org.
"""
import argparse
import gzip
import json
import re
import sys
import urllib.request

NUGET_ORG = "https://api.nuget.org/v3/index.json"


# --- Versions --------------------------------------------------------------------------------------

def parse_version(text):
    """A NuGet version as a sortable key: (numbers padded to four, is-release, prerelease identifiers)."""
    text = text.strip().split("+", 1)[0]
    core, _, pre = text.partition("-")
    numbers = [int(n) for n in core.split(".")]
    numbers += [0] * (4 - len(numbers))
    labels = tuple((0, int(p), "") if p.isdigit() else (1, 0, p.lower()) for p in pre.split(".")) if pre else ()
    return (tuple(numbers), not pre, labels)


def is_prerelease(text):
    return "-" in text.split("+", 1)[0]


def parse_range(text):
    """NuGet interval notation, e.g. '[10.0.0, 10.0.12]', '(, 9.0.20]', '[2.0.0, )'. A bare version means
    'that version or later', as in a PackageReference."""
    text = text.strip()
    if not text or text[0] not in "[(":
        return (parse_version(text), True, None, False)
    low_inclusive, high_inclusive = text[0] == "[", text[-1] == "]"
    inner = text[1:-1]
    if "," not in inner:
        version = parse_version(inner)
        return (version, True, version, True)
    low, high = (part.strip() for part in inner.split(",", 1))
    return (parse_version(low) if low else None, low_inclusive, parse_version(high) if high else None, high_inclusive)


def in_range(version_key, interval):
    low, low_inclusive, high, high_inclusive = interval
    if low is not None and (version_key < low or (version_key == low and not low_inclusive)):
        return False
    if high is not None and (version_key > high or (version_key == high and not high_inclusive)):
        return False
    return True


def advisories_for(version, advisories):
    key = parse_version(version)
    return sorted({a["url"] for a in advisories if in_range(key, parse_range(a["versions"]))})


def first_fixed(current, versions, advisories, prerelease=False):
    """(same line, newer): the lowest safe published versions above `current`, or None."""
    current_key = parse_version(current)
    candidates = sorted(
        (v for v in versions if parse_version(v) > current_key and (prerelease or not is_prerelease(v))),
        key=parse_version)
    safe = [v for v in candidates if not advisories_for(v, advisories)]
    same_line = next((v for v in safe if parse_version(v)[0][:2] == current_key[0][:2]), None)
    return same_line, (safe[0] if safe else None)


# --- Feed ------------------------------------------------------------------------------------------

def fetch_json(url):
    request = urllib.request.Request(url, headers={"Accept-Encoding": "gzip", "User-Agent": "first_fixed.py"})
    with urllib.request.urlopen(request, timeout=60) as response:
        body = response.read()
        if response.headers.get("Content-Encoding") == "gzip" or body[:2] == b"\x1f\x8b":
            body = gzip.decompress(body)
    return json.loads(body)


def resource(index, kind):
    for entry in index["resources"]:
        if entry["@type"].startswith(kind):
            return entry["@id"]
    return None


def published_versions(feed_index, package):
    base = resource(feed_index, "PackageBaseAddress")
    if base is None:
        raise SystemExit("the feed has no PackageBaseAddress resource")
    return fetch_json(f"{base.rstrip('/')}/{package.lower()}/index.json")["versions"]


def known_advisories(vulnerability_index_url, package):
    """Every advisory the feed lists for the package: the base file plus the updates since."""
    index = fetch_json(vulnerability_index_url)
    found = []
    for part in index:
        data = fetch_json(part["@id"])
        found += data.get(package.lower(), [])
    return found


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("package")
    parser.add_argument("current")
    parser.add_argument("--feed", default=NUGET_ORG, help="service index of the feed you restore from")
    parser.add_argument("--vulnerability-feed", help="service index to read advisories from (default: --feed)")
    parser.add_argument("--prerelease", action="store_true", help="consider prerelease versions")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    try:
        feed = fetch_json(args.feed)
        vulnerability_feed = fetch_json(args.vulnerability_feed) if args.vulnerability_feed else feed
        vulnerability_index = resource(vulnerability_feed, "VulnerabilityInfo")
        if vulnerability_index is None:
            print("This feed publishes no vulnerability data: pass --vulnerability-feed "
                  f"{NUGET_ORG}", file=sys.stderr)
            return 2
        versions = published_versions(feed, args.package)
        advisories = known_advisories(vulnerability_index, args.package)
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    hit = advisories_for(args.current, advisories)
    same_line, newer = first_fixed(args.current, versions, advisories, args.prerelease)
    if args.json:
        print(json.dumps({"package": args.package, "current": args.current, "advisories": hit,
                          "sameLine": same_line, "newer": newer}, indent=2))
    else:
        print(f"{args.package} {args.current}: " + ("; ".join(hit) if hit else "no known advisory"))
        print(f"  same line: {same_line or 'none'}")
        print(f"  newer:     {newer or 'none'}")
        if newer is None:
            print("  No safe version is published yet: record the risk decision instead of pinning.")
    return 0 if newer else 1


if __name__ == "__main__":
    sys.exit(main())
