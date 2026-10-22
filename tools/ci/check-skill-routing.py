"""Do the skill descriptions still point the right way?

A skill is loaded because its description matches what the developer asked. Two things go wrong as
skills are added and edited: a description drifts away from the requests it should answer, and two
descriptions grow so alike that either could be picked. This checks both, offline and in a second:

  1. Routing. Each line of tests/routing/requests.tsv is a skill and a request a developer might type.
     Every description is scored against the request (TF-IDF over words, with light stemming), and the
     request's skill has to be in the top 3. The rank-1 rate is printed, so a drop shows in the log.
  2. Overlap. Every pair of descriptions more alike than OVERLAP fails, unless
     tests/routing/overlap-allowed.tsv lists the pair with the reason they're meant to sit close.

This is a lexical stand-in for the model's choice, not the model: a pass means no description has
lost its words, not that routing is perfect. Every core skill needs at least one request.

    python tools/ci/check-skill-routing.py [--verbose]
"""
import math
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SKILLS = ROOT / ".claude" / "skills"
REQUESTS = ROOT / "tests" / "routing" / "requests.tsv"
ALLOWED = ROOT / "tests" / "routing" / "overlap-allowed.tsv"
TOP = 3
OVERLAP = 0.45

STOP = set("""a an and are as at be but by can do does for from how i if in into is it its me my no not of
on or our so that the their them then there these this to us use used uses using was we what when where
which who why will with without you your it's don't isn't can't i'm we're""".split())


def stem(word):
    """Crude, but the same on both sides: caching/cache, layers/layer, smaller/small, policies/policy."""
    if len(word) > 4 and word.endswith("ies"):
        word = word[:-3] + "y"
    elif len(word) > 3 and word.endswith("s") and not word.endswith("ss"):
        word = word[:-1]
    for suffix in ("ing", "ed", "er"):
        if len(word) > len(suffix) + 3 and word.endswith(suffix):
            word = word[: -len(suffix)]
            break
    return word[:-1] if len(word) > 3 and word.endswith("e") else word


def words(text):
    return [stem(w) for w in re.findall(r"[a-z0-9]+(?:\.[a-z0-9]+)*", text.lower()) if w not in STOP]


def descriptions():
    found = {}
    for path in sorted(SKILLS.glob("*/SKILL.md")):
        match = re.search(r"^description:\s*(.+)$", path.read_text(encoding="utf-8"), re.M)
        if match:
            name = path.parent.name
            found[name] = f"{name.replace('-', ' ')} {match.group(1)}"
    return found


def vectors(texts):
    """TF-IDF vectors, normalised, for a dict of name -> text; returns (vectors, idf)."""
    bags = {name: words(text) for name, text in texts.items()}
    df = {}
    for bag in bags.values():
        for term in set(bag):
            df[term] = df.get(term, 0) + 1
    idf = {term: math.log((1 + len(bags)) / (1 + count)) + 1 for term, count in df.items()}
    return {name: weigh(bag, idf) for name, bag in bags.items()}, idf


def weigh(bag, idf):
    counts = {}
    for term in bag:
        if term in idf:
            counts[term] = counts.get(term, 0) + 1
    vector = {term: (1 + math.log(c)) * idf[term] for term, c in counts.items()}
    norm = math.sqrt(sum(v * v for v in vector.values())) or 1.0
    return {term: v / norm for term, v in vector.items()}


def cosine(a, b):
    if len(a) > len(b):
        a, b = b, a
    return sum(v * b.get(term, 0.0) for term, v in a.items())


def read_tsv(path):
    rows = []
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip() and not line.startswith("#"):
                rows.append([part.strip() for part in line.split("\t")])
    return rows


def rank(request, skill, docs, idf):
    query = weigh(words(request), idf)
    scored = sorted(docs, key=lambda name: -cosine(query, docs[name]))
    return scored.index(skill) + 1, scored[:TOP]


def main(argv=None):
    verbose = "--verbose" in (sys.argv[1:] if argv is None else argv)
    texts = descriptions()
    docs, idf = vectors(texts)
    problems = []

    samples = read_tsv(REQUESTS)
    covered = set()
    first = 0
    for row in samples:
        if len(row) != 2:
            problems.append(f"requests.tsv: expected 'skill<TAB>request': {row}")
            continue
        skill, request = row
        if skill not in docs:
            problems.append(f"requests.tsv: no skill named {skill}")
            continue
        if "-" in skill and skill.replace("-", " ") in request.lower().replace("-", " "):
            problems.append(f"requests.tsv: the request names its skill: {request}")
        covered.add(skill)
        place, top = rank(request, skill, docs, idf)
        first += place == 1
        if place > TOP:
            problems.append(f"routing: {skill} is #{place} for '{request}' (top: {', '.join(top)})")
        elif verbose and place > 1:
            print(f"  #{place} {skill}: '{request}' (top: {', '.join(top)})")
    for skill in sorted(set(docs) - covered):
        problems.append(f"routing: {skill} has no request in requests.tsv")

    allowed = {}
    for row in read_tsv(ALLOWED):
        if len(row) != 3 or not row[2]:
            problems.append(f"overlap-allowed.tsv: expected 'skill<TAB>skill<TAB>reason': {row}")
            continue
        allowed[frozenset(row[:2])] = row[2]
    names = sorted(docs)
    used = set()
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            score = cosine(docs[a], docs[b])
            pair = frozenset((a, b))
            if score > OVERLAP:
                if pair in allowed:
                    used.add(pair)
                else:
                    problems.append(f"overlap: {a} and {b} are {score:.2f} alike: make them distinct, "
                                    "or list the pair with its reason in overlap-allowed.tsv")
            elif verbose and score > OVERLAP - 0.1:
                print(f"  close: {a} / {b} {score:.2f}")
    for pair in set(allowed) - used:
        problems.append(f"overlap-allowed.tsv: {' / '.join(sorted(pair))} no longer overlaps: remove the line")

    total = len(samples) or 1
    print(f"skill routing: {len(docs)} skills, {len(samples)} requests, "
          f"{first}/{len(samples)} first ({100 * first // total}%), all in the top {TOP} unless listed below")
    for problem in problems:
        print(f"::error::{problem}" if "GITHUB_ACTIONS" in __import__("os").environ else problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
