#!/usr/bin/env python3
"""Checks a story check before anyone reads it, then prints it.

    python story_check.py report.json --story story.md [--repo DIR] [--json]

report.json has one entry per routine item (and any the team adds):

    {"items": [
      {"item": "empty",       "verdict": "stated",  "quote": "words copied from the story"},
      {"item": "failure",     "verdict": "code",    "where": "src/Visits/BookVisit.cs:42", "snippet": "catch (TimeoutRejectedException"},
      {"item": "duplicates",  "verdict": "gap",     "question": "What happens when the same visit is booked twice?"},
      {"item": "interruption","verdict": "n/a",     "reason": "one request, nothing to resume"}]}

It refuses (exit 1, each problem listed):
- a routine item with no entry, or an unknown verdict;
- a `stated` quote that isn't in the story, word for word (spacing, case and Arabic letter forms aside);
- a `code` place whose file doesn't exist or whose line doesn't hold the snippet (within two lines);
- a `gap` with no question, an `n/a` with no reason;
- a question a sentence of the story may already answer, unless `not_answered_because` says why it doesn't.
Otherwise (exit 0) it prints the ruling, READY or NOT READY with the questions, and the table.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path

ROUTINE = {
    "empty": "nothing there yet: no records, a first use, an empty list",
    "maximum": "the most: the longest text, the largest number, the most items, the last slot",
    "failure": "a dependency slow or down, a timeout, a partial failure",
    "permission": "who may do it, and who may not: roles, ownership, eligibility",
    "duplicates": "the same request twice, or two at once",
    "interruption": "stopped halfway: closed, cancelled, retried, resumed",
    "existing-data": "records made before this story, and how they move to the new shape",
    "language": "Arabic and English: text, direction, dates, digits",
    "touches": "what else depends on what this changes",
}
VERDICTS = ("stated", "code", "gap", "n/a")
STOP = set("""a an the and or of to in on for at by with from is are be was were it its this that these those as
do does did can could should would will shall may might must what which who whom whose when where why how if then
than there their they them we our you your i he she his her not no yes any all each every some more most such
only also into out up down over under about after before again once same other own just so too very ما ماذا من
في على الى إلى عن هل لو اذا إذا او أو و ثم لا لم لن هو هي هم ان أن كان تكون يكون التي الذي هذا هذه ذلك تلك""".split())
ARABIC_MARKS = re.compile(r"[ً-ٰٟـ]")


def normalize(text: str) -> str:
    text = unicodedata.normalize("NFKC", text).lower()
    text = ARABIC_MARKS.sub("", text)
    text = re.sub("[أإآ]", "ا", text).replace("ى", "ي").replace("ة", "ه")
    return re.sub(r"\s+", " ", re.sub(r"[^\w\s]", " ", text)).strip()


def words(text: str) -> set[str]:
    return {w for w in normalize(text).split() if w not in STOP and len(w) > 2}


def sentences(story: str) -> list[str]:
    parts = re.split(r"(?<=[.!?؟])\s+|\n+", story)
    return [p.strip(" -*#\t") for p in parts if len(words(p)) >= 2]


def answered_by(question: str, story_sentences: list[str]) -> str | None:
    """The story sentence that shares most of the question's words, if it shares enough to be its answer."""
    q = words(question)
    if len(q) < 2:
        return None
    best, score = None, 0.0
    for s in story_sentences:
        overlap = len(q & words(s)) / len(q)
        if overlap > score:
            best, score = s, overlap
    return best if score >= 0.6 else None


def check_code(repo: Path, where: str, snippet: str) -> str | None:
    m = re.match(r"^(.+?):(\d+)$", where.strip())
    if not m:
        return f"`{where}` isn't a file:line"
    path = repo / m.group(1).replace("\\", "/")
    if not path.is_file():
        return f"{m.group(1)} doesn't exist"
    lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    n = int(m.group(2))
    if not 1 <= n <= len(lines):
        return f"{m.group(1)} has {len(lines)} lines, not {n}"
    if not snippet.strip():
        return f"{where} needs a snippet: the code that handles the case"
    near = " ".join(lines[max(0, n - 3):n + 2])
    if normalize(snippet) not in normalize(near):
        return f"{where} doesn't hold `{snippet}`"
    return None


def check(report: dict, story: str, repo: Path) -> tuple[list[str], list[dict]]:
    problems, items = [], report.get("items", [])
    seen = {i.get("item") for i in items}
    for name in ROUTINE:
        if name not in seen:
            problems.append(f"{name}: no entry ({ROUTINE[name]})")
    story_norm, story_sentences = normalize(story), sentences(story)
    for i in items:
        name, verdict = i.get("item", "?"), i.get("verdict")
        if verdict not in VERDICTS:
            problems.append(f"{name}: verdict `{verdict}` isn't one of {', '.join(VERDICTS)}")
        elif verdict == "stated":
            quote = i.get("quote", "")
            if not normalize(quote) or normalize(quote) not in story_norm:
                problems.append(f"{name}: the quote isn't in the story: \"{quote}\"")
        elif verdict == "code":
            if (err := check_code(repo, i.get("where", ""), i.get("snippet", ""))):
                problems.append(f"{name}: {err}")
        elif verdict == "gap":
            question = i.get("question", "").strip()
            if not question:
                problems.append(f"{name}: a gap needs the question to ask")
            elif (s := answered_by(question, story_sentences)) and not i.get("not_answered_because", "").strip():
                problems.append(f"{name}: the story may already answer this: \"{s}\". Drop the question, or say in "
                                "`not_answered_because` why that sentence doesn't answer it")
        elif verdict == "n/a" and not i.get("reason", "").strip():
            problems.append(f"{name}: n/a needs a reason")
    return problems, items


def render(items: list[dict]) -> str:
    gaps = [i for i in items if i.get("verdict") == "gap"]
    out = [f"Story check: {'READY' if not gaps else f'NOT READY ({len(gaps)} question' + ('s' if len(gaps) > 1 else '') + ')'}"]
    if gaps:
        out += ["", "Answer these before building:"] + [f"{n}. {g['question']} ({g['item']})" for n, g in enumerate(gaps, 1)]
    out += ["", "| Case | Where it's settled |", "|---|---|"]
    for i in items:
        v = i.get("verdict")
        where = {"stated": f"the story: \"{i.get('quote', '')}\"", "code": f"the code: {i.get('where', '')}",
                 "gap": "**open**", "n/a": f"doesn't apply: {i.get('reason', '')}"}.get(v, v)
        out.append(f"| {i.get('item')} | {where} |")
    return "\n".join(out)


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("report", type=Path)
    p.add_argument("--story", type=Path, required=True)
    p.add_argument("--repo", type=Path, default=Path("."))
    p.add_argument("--json", action="store_true")
    a = p.parse_args(argv)
    report = json.loads(a.report.read_text(encoding="utf-8-sig"))
    problems, items = check(report, a.story.read_text(encoding="utf-8-sig"), a.repo)
    if a.json:
        print(json.dumps({"problems": problems, "ready": not problems and not any(i.get("verdict") == "gap" for i in items)},
                         indent=2, ensure_ascii=False))
    elif problems:
        print("The story check isn't ready to show:")
        print("\n".join(f"- {x}" for x in problems))
    else:
        print(render(items))
    return 1 if problems else 0


if __name__ == "__main__":
    # A Windows console is cp1252: an Arabic criterion or line of code would stop the script mid-report.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
