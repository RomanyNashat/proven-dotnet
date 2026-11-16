# The skill-hub eval

The listing-budget ADR (`docs/decisions/skill-listing-budget.md`) keeps hub skills as the next step for when
the listing runs out of room, but only after an eval shows their extra hop doesn't lose routing. This is
that eval.

**The two listings** (`build.py`), from the core as it stands:
- **flat:** today's listing: every skill and command (83 entries, 12,328 characters).
- **hubs:** the same commands, plus nine hubs (`hubs.json`) listed instead of the 63 skills (29 entries,
  3,990 characters). Each hub is a table that points to its members' SKILL.md files. The members are copied
  with `disable-model-invocation: true`, so they're out of the listing and the Skill tool, and the model
  reaches one only by following the table.

**A run** (`run.sh`): one headless session per request, in a fresh copy of the arm, with only the project's
settings, so the installed skills don't mix in. The requests come from `requests.tsv`: the first request
`tests/routing/requests.tsv` has for 18 skills, two per hub (one for the stories hub). One appended line,
the same for both arms, says the folder is empty and asks the model to open the skill that fits and name it.
`grade.py` reads which skill the model opened first, through the Skill tool or by reading its SKILL.md.

## The bar, written before any run

The hubs pass the pilot when all three hold:
1. **Routing:** hubs open the right member skill for at least as many requests as flat does, minus one
   (of 18).
2. **The hop is followed:** in at least 16 of 18 hub runs, a hub is opened and then a member.
3. **The cost:** the hop adds at most one turn per request on average.

If the hubs pass, the grid comes next: every request in `tests/routing/requests.tsv`, three runs each, and a
noise floor, before anything moves. If they fail, the ADR keeps the flat listing and says why. If flat
itself scores under 16 of 18, the requests are too hard to compare on, and they get redesigned first.

## Running it

```bash
bash tests/evals/skill-hubs/run.sh flat tests/evals/skill-hubs/runs/pilot
bash tests/evals/skill-hubs/run.sh hubs tests/evals/skill-hubs/runs/pilot
python tests/evals/skill-hubs/grade.py tests/evals/skill-hubs/runs/pilot
```

Results are in `results.md`.
