#!/usr/bin/env python3
"""A fixed problem written down in the service repo (`docs/solutions/`), so the next person, or the next
session, finds it before solving it again. The developer commits the note with the fix.

    python solutions.py new --from note.json        # title, symptoms, cause, fix, tests, files, tags
    python solutions.py find "redis timeout on start-up" [--file src/A.cs]
    python solutions.py check                        # every note has its sections

Long numbers and emails are masked in what it writes. A note is what was seen, why, and what fixed it:
never a patient's data, a secret or a person's name.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

FOLDER = Path("docs") / "solutions"
REQUIRED = ("title", "symptoms", "cause", "fix")
SECTIONS = ("Symptoms", "Cause", "Fix", "Tests")
LONG_NUMBER = re.compile(r"\d{7,}")
EMAIL = re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+")
WORD = re.compile(r"[a-z0-9]+")
STOP = {"the", "a", "an", "on", "in", "of", "to", "and", "or", "is", "it", "for", "with", "at", "by", "from", "though"}


def mask(text: str) -> str:
    return EMAIL.sub("<email>", LONG_NUMBER.sub("<number>", text or "")).strip()


def slug(title: str) -> str:
    return "-".join(WORD.findall(title.lower()))[:70].strip("-") or "note"


def as_list(value) -> list[str]:
    items = value if isinstance(value, list) else re.split(r"[,\n]", value or "")
    return [i.strip().replace("\\", "/") for i in items if i and i.strip()]


def new(repo: Path, note: dict) -> Path:
    missing = [k for k in REQUIRED if not str(note.get(k) or "").strip()]
    if missing:
        raise ValueError(f"a note needs {', '.join(missing)}: what was seen, why, and what fixed it")
    folder = repo / FOLDER
    folder.mkdir(parents=True, exist_ok=True)
    day = datetime.now(timezone.utc).date().isoformat()
    path, n = folder / f"{day}-{slug(note['title'])}.md", 2
    while path.exists():
        path, n = folder / f"{day}-{slug(note['title'])}-{n}.md", n + 1
    files, tags = as_list(note.get("files")), as_list(note.get("tags"))
    lines = ["---", f"title: {mask(note['title'])}", f"date: {day}", f"files: [{', '.join(files)}]",
             f"tags: [{', '.join(tags)}]", "---", "", f"# {mask(note['title'])}", ""]
    for section in SECTIONS:
        body = mask(str(note.get(section.lower()) or ""))
        lines += [f"## {section}", "", body or "(none written)", ""]
    path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
    return path


def notes(repo: Path) -> list[tuple[Path, str]]:
    folder = repo / FOLDER
    return [(p, p.read_text(encoding="utf-8-sig", errors="replace")) for p in sorted(folder.glob("*.md"))] if folder.is_dir() else []


def field(text: str, name: str) -> str:
    m = re.search(rf"^{name}:\s*(.*)$", text, re.M)
    return m.group(1).strip() if m else ""


def section(text: str, name: str) -> str:
    m = re.search(rf"^## {name}\s*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return m.group(1).strip() if m else ""


def find(repo: Path, words: str, file: str | None) -> list[tuple[int, Path, str]]:
    wanted = {w for w in WORD.findall(words.lower()) if w not in STOP}
    target = (file or "").replace("\\", "/").lstrip("./")
    ranked = []
    for path, text in notes(repo):
        title = field(text, "title")
        title_words = set(WORD.findall(title.lower()))
        body_words = set(WORD.findall((section(text, "Symptoms") + " " + field(text, "tags")).lower()))
        score = 3 * len(wanted & title_words) + 2 * len(wanted & body_words) + len(wanted & set(WORD.findall(text.lower())))
        if target and target in field(text, "files").replace("\\", "/"):
            score += 10
        if score:
            ranked.append((score, path, text))
    return sorted(ranked, key=lambda r: (-r[0], r[1].name))[:5]


def check(repo: Path) -> list[str]:
    problems = []
    for path, text in notes(repo):
        if not field(text, "title"):
            problems.append(f"{path.name}: no title")
        problems += [f"{path.name}: no ## {s}" for s in ("Symptoms", "Cause", "Fix") if not section(text, s)]
    return problems


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="action", required=True)
    n = sub.add_parser("new"); n.add_argument("--from", dest="source", required=True)
    f = sub.add_parser("find"); f.add_argument("words", nargs="?", default=""); f.add_argument("--file")
    sub.add_parser("check")
    for p in sub.choices.values():
        p.add_argument("--repo", default=".")
    args = ap.parse_args(argv)
    repo = Path(args.repo)
    if args.action == "new":
        try:
            path = new(repo, json.loads(Path(args.source).read_text(encoding="utf-8-sig")))
        except ValueError as e:
            print(e, file=sys.stderr)
            return 2
        print(f"{path.relative_to(repo).as_posix()}: the developer commits it with the fix")
        return 0
    if args.action == "find":
        found = find(repo, args.words, args.file)
        if not found:
            print("No note in docs/solutions matches.")
        for _, path, text in found:
            print(f"{path.relative_to(repo).as_posix()}: {field(text, 'title')} | cause: {section(text, 'Cause')[:120]} | fix: {section(text, 'Fix')[:120]}")
        return 0
    problems = check(repo)
    for p in problems:
        print(p)
    if not problems:
        print(f"{len(notes(repo))} notes, each with its sections.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
