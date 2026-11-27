---
name: story-check
description: "Check a story before building it: the cases it leaves open (empty, failure, permission, duplicates, existing data), grounded in the code, as questions."
version: 1.2.0
argument-hint: "[Jira key | path to story.md]"
---

# Story check: what the story leaves open, before we build it

A story is checked once, before `qa-engineer` designs its tests and before any code. The check answers one
question: **is there a case the story, and the code it touches, leave open?** Each open case becomes one
question for whoever owns the story. Nothing else goes in: no wording, no taste, no "consider adding".

## Started as `/story-check KEY` or `/story-check story.md`

1. **Get the story's text, and only from where the developer pointed.**
   - A path: read that file.
   - A Jira key: read the issue through a connected Jira tool, **read only**: its summary, description,
     acceptance criteria and the comments of whoever owns the story. Save them to `.qa/story.md`, with the
     key at the top, so the quotes can be checked.
   - No Jira tool connected, or the read fails: say so, and ask the developer to paste the story's text.
     Never check a story from its key alone, from memory or from the branch name: there is nothing to
     quote.
2. Run the routine below against the story and the code in this folder, then check it with the script.
3. Show what the script prints, and where the story came from. Nothing goes back to Jira: no comment,
   no field change, no transition. The developer takes the questions to whoever owns the story.

## The rules
- **Only open cases.** A case is settled when the story says what happens (quote it) or the code already
  handles it (cite the line). A case that doesn't apply says why in a few words. Everything else is a gap.
- **A written happy path is not a settled failure path.** "The patient cancels the visit" says nothing about
  the store being down; that silence is a gap.
- **The story first, then the code.** Look for the answer in the story's text, then in the code it touches
  (`find_symbol`, `find_callers`; text search only if Roslyn isn't there). A case the code already handles is
  settled, even if the story never mentions it. A service with a `rules.md` (`business-rules`) has its
  rules written down already: cite the rule's line of code.
- **One question per gap, and a question a person can answer in a sentence.** It names the case and the
  choice: "If the booking store is down, does the app say so or keep the visit shown as booked?", not
  "What about errors?".
- **Never ask what the story answers.** The script below refuses a question a story sentence may answer.
  If that sentence really doesn't answer it (it says 2 hours, not whose clock), say so in
  `not_answered_because`.

## The routine

| Item | What it asks |
|---|---|
| `empty` | Nothing there yet: no records, a first use, an empty list. |
| `maximum` | The most: the longest text, the largest number, the most items, the last slot. |
| `failure` | A dependency slow or down, a timeout, a partial failure. |
| `permission` | Who may do it and who may not: roles, ownership, eligibility. |
| `duplicates` | The same request twice, or two at once. |
| `interruption` | Stopped halfway: closed, cancelled, retried, resumed. |
| `existing-data` | Records made before this story, and how they move to the new shape. |
| `language` | Arabic and English: text, direction, dates, digits. |
| `touches` | What else depends on what this changes (`find_callers` on what the story changes). |

A team layer adds its own items (a healthcare service adds consent, for example) as a file of
`name: what it asks` lines, and its rules say where. Pass it with `--items <file>`: the script then
requires those items like the routine's, and checks them the same way.

## Writing it down, and checking it before anyone reads it

Write one entry per item to `.qa/story-check.json` (output, kept out of git):

```json
{"items": [
  {"item": "duplicates", "verdict": "stated", "quote": "Cancelling a visit that is already cancelled changes nothing"},
  {"item": "permission", "verdict": "code", "where": "src/Visits/CancelVisit.cs:8", "snippet": "visit.PatientId != patientId"},
  {"item": "failure", "verdict": "gap", "question": "If the booking store is down, does the app say so or keep the visit shown as booked?"},
  {"item": "interruption", "verdict": "n/a", "reason": "one request, nothing to resume"}
]}
```

Then:

```bash
python ~/.claude/skills/story-check/scripts/story_check.py .qa/story-check.json --story story.md --repo .
```

It refuses a check with an item missing, a quote the story doesn't have word for word, a line of code
that doesn't hold its snippet, a gap without a question, an n/a without a reason, or a question the story
may already answer. Fix each one it lists and run it again. **A check it refuses is never shown.** What it
prints once it passes is the report: the ruling (READY, or NOT READY with the questions numbered), then
where each case is settled.

## Checking the plan, after the story

Once the story is READY and the planner has written `plan.md`, the plan is checked the same way, before any
code:

```bash
python ~/.claude/skills/story-check/scripts/plan_check.py plan.md --story story.md --repo .
```

- **Scope:** every task names the criteria it serves (`**Serves**: AC1, AC3`) or says why the story needs it
  (`enabler: ...`); every criterion has a task. A task beyond the story is a question for its owner.
- **Design against what exists:** `## Builds on` cites what the repo already has (`uses: path:line `code``,
  checked against the file) and gives a reason for each new table or class (`new: ... — because ...`). A
  new one the repo already defines is named with its place.
- **The API's callers:** a plan that adds or changes an endpoint shows an example, the errors, and what old
  app versions see, so the mobile team can build against it before the code exists.

It exits 1 with the gaps numbered. A refused plan goes back to the planner, not to `tdd-guide`.

## After the check
- **NOT READY:** the questions go to whoever owns the story. Their answers go into `story.md` as criteria
  (`AC5: ...`), and the story is checked again.
- **READY:** `qa-engineer` designs the cases from the story (`test-design`).
