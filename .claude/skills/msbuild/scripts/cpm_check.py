"""Check a move onto Central Package Management: nothing lost, every version change on purpose.

    python cpm_check.py snapshot <solution folder> > before.json     # before the move
    python cpm_check.py compare before.json <solution folder>         # after it

`snapshot` records every package version the projects and props files state (PackageReference,
PackageVersion, GlobalPackageReference), as written: a range stays a range. `compare` reads the
`Directory.Packages.props` files after the move and reports:
  - ERROR  a package that has no central version any more;
  - ERROR  a version range that didn't survive as written. A range is a fence (for example
           FluentAssertions [7.0.0,8.0.0), which keeps a licence-bound major out); a tool that only
           understands exact versions drops it, and the restore that fails afterwards invites "fixing"
           it with the newest version;
  - CHANGE a project whose version moves, because projects disagreed and one version was picked, or a
           floating version (4.*) that became an exact one (CPM refuses floats, NU1011). Each is an
           upgrade or downgrade of that project, which its tests have to prove.
Exit code 1 when there is an ERROR. Standard library only.
"""
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

SKIP = {"bin", "obj", ".git", "node_modules", "artifacts"}
FILES = (".csproj", ".fsproj", ".vbproj", ".props", ".targets")
ITEMS = ("PackageReference", "PackageVersion", "GlobalPackageReference")


def local(tag):
    return tag.rsplit("}", 1)[-1]


def is_fence(version):
    """A version range: brackets around more than one exact version."""
    v = version.strip()
    return bool(v) and v[0] in "[(" and not re.fullmatch(r"\[[^,\]]+\]", v)


def project_files(root):
    for path in sorted(pathlib.Path(root).rglob("*")):
        if path.suffix.lower() in FILES and path.is_file() and not SKIP.intersection(path.relative_to(root).parts[:-1]):
            yield path


def entries(root):
    """Every stated package version: (file, item kind, package, version as written, condition)."""
    found = []
    for path in project_files(root):
        try:
            tree = ET.parse(path)
        except ET.ParseError:
            continue
        for group in tree.getroot().iter():
            if local(group.tag) != "ItemGroup":
                continue
            condition = (group.get("Condition") or "").strip()
            for item in group:
                kind = local(item.tag)
                if kind not in ITEMS:
                    continue
                package = item.get("Include") or item.get("Update")
                version = item.get("Version") or item.get("VersionOverride")
                if version is None:
                    child = next((c for c in item if local(c.tag) in ("Version", "VersionOverride")), None)
                    version = child.text if child is not None else None
                if package and version:
                    found.append({"file": path.relative_to(root).as_posix(), "kind": kind, "package": package,
                                  "version": version.strip(), "condition": condition})
    return found


def central(root):
    """Package versions after the move: Directory.Packages.props, plus any VersionOverride left in projects."""
    versions = {}
    for entry in entries(root):
        if entry["kind"] in ("PackageVersion", "GlobalPackageReference") or entry["file"].endswith("Directory.Packages.props"):
            versions.setdefault((entry["package"].lower(), entry["condition"]), entry["version"])
    return versions


def compare(before, root):
    after = central(root)
    errors, changes = [], []
    for entry in before:
        key = (entry["package"].lower(), entry["condition"])
        now = after.get(key) or after.get((key[0], ""))
        where = f'{entry["file"]}: {entry["package"]} {entry["version"]}'
        if now is None:
            errors.append(f"{where}: no central version any more")
        elif is_fence(entry["version"]) and now != entry["version"]:
            errors.append(f"{where}: the range became {now}")
        elif now != entry["version"]:
            changes.append(f"{where} -> {now}")
    return errors, changes


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) == 2 and argv[0] == "snapshot":
        print(json.dumps(entries(argv[1]), indent=2))
        return 0
    if len(argv) == 3 and argv[0] == "compare":
        before = json.loads(pathlib.Path(argv[1]).read_text(encoding="utf-8-sig"))
        errors, changes = compare(before, argv[2])
        for line in errors:
            print(f"ERROR  {line}")
        for line in changes:
            print(f"CHANGE {line}")
        if not errors and not changes:
            print("Every package kept its version.")
        return 1 if errors else 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
