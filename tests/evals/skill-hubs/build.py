#!/usr/bin/env python3
"""Builds the two listings the hub eval compares, as project folders a headless session opens:

    python build.py <out folder>    ->  <out>/flat/.claude/...   <out>/hubs/.claude/...

flat: the core's skills and commands, listed as installed today.
hubs: the same commands; the nine hubs of hubs.json listed; every member skill copied with
      `disable-model-invocation: true`, so it is out of the listing and the Skill tool, and reached only by
      the hub's table telling the model to read it. That is the hop the eval measures.
Prints each listing's size, as check-listing-budget.py counts it.
"""
import json
import re
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CORE = HERE.parents[2] / ".claude"
sys.path.insert(0, str(HERE.parents[2] / "tools" / "ci"))


def description(skill_md: str) -> str:
    m = re.search(r'^description:\s*"?(.*?)"?\s*$', skill_md, re.M)
    return m.group(1) if m else ""


def hide(skill_md: str) -> str:
    return re.sub(r"^---\n", "---\ndisable-model-invocation: true\n", skill_md, count=1)


def hub_md(name: str, hub: dict) -> str:
    rows = []
    for member in hub["members"]:
        text = (CORE / "skills" / member / "SKILL.md").read_text(encoding="utf-8")
        rows.append(f"| {description(text)} | `.claude/skills/{member}/SKILL.md` |")
    return (f'---\nname: {name}\ndescription: "{hub["description"]}"\n---\n\n# {name}\n\n'
            "Pick the row that fits the request, then read that skill's SKILL.md with the Read tool and follow "
            "it. It is not in the skill listing, so the Skill tool can't open it.\n\n"
            "| When | Read |\n|---|---|\n" + "\n".join(rows) + "\n")


def build(out: Path) -> dict:
    hubs = json.loads((HERE / "hubs.json").read_text(encoding="utf-8"))
    for arm in ("flat", "hubs"):
        target = out / arm / ".claude"
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(CORE / "commands", target / "commands")
        for skill in (CORE / "skills").iterdir():
            dest = target / "skills" / skill.name
            shutil.copytree(skill, dest, ignore=shutil.ignore_patterns("__pycache__"))
            if arm == "hubs":
                md = dest / "SKILL.md"
                md.write_text(hide(md.read_text(encoding="utf-8")), encoding="utf-8")
        if arm == "hubs":
            clash = set(hubs) & ({p.stem for p in (CORE / "commands").glob("*.md")} | {p.name for p in (CORE / "skills").iterdir()})
            if clash:
                sys.exit(f"a hub has the name of a command or skill: {sorted(clash)}")
            for name, hub in hubs.items():
                (target / "skills" / name).mkdir(parents=True)
                (target / "skills" / name / "SKILL.md").write_text(hub_md(name, hub), encoding="utf-8")
    return {arm: listed_chars(out / arm / ".claude") for arm in ("flat", "hubs")}


def listed_chars(claude: Path) -> dict:
    """What the model sees listed: name and description of each skill or command it may start itself."""
    entries = []
    for md in [*claude.glob("skills/*/SKILL.md"), *claude.glob("commands/*.md")]:
        text = md.read_text(encoding="utf-8")
        if re.search(r"^disable-model-invocation:\s*true", text, re.M):
            continue
        name = md.parent.name if md.name == "SKILL.md" else md.stem
        entries.append(f"- {name}: {description(text)}")
    return {"entries": len(entries), "chars": sum(len(e) for e in entries)}

if __name__ == "__main__":
    out = Path(sys.argv[1])
    print(json.dumps(build(out), indent=1, default=str))
