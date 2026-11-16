---
name: problem-analysis
description: "Find a root cause (5 whys, evidence each), choose between designs (weighted criteria, how close), or pre-mortem a plan, into an ADR."
version: 1.2.0
---

# Problem analysis: the method that fits the problem

Three kinds of problem, three methods. Pick by what is being asked:

| The problem | The method |
|---|---|
| Why did this happen? (an incident, a bug that keeps coming back) | 5 whys, each with its evidence |
| Which option? (a library, a design, a migration path) | A weighted trade-off, then how close it is |
| Will this plan hold? (before a release, a migration, a cutover) | A pre-mortem |

The result goes into the lightweight ADR (`~/.claude/proven/adr-template.md`), in the repo's decision folder.

## 5 whys, each with its evidence

Each why is the cause of the one above it, and each names its evidence: a log line with its time, a
commit, a metric, a file and line, a ticket.

```
1. Patients got reminders a day late.         evidence: 212 complaints on 2026-11-09 (support export)
2. The reminder job ran at 21:00 UTC, not 21:00 local.   evidence: job log 2026-11-08T21:00:03Z
3. Its cron has no time zone.                  evidence: Jobs/ReminderJob.cs:14, `0 21 * * *`
4. The image has no tzdata, so a zone couldn't be set.   evidence: the image's package list
5. Nothing checks a job's zone before release.         evidence: no such item in the release checklist
```

- **No evidence, no why.** A why without it is a guess: write `guess: check <what>` and check it before
  going deeper.
- **Test each link:** if this cause hadn't happened, would the one above still have? If yes, it isn't its
  cause.
- **Stop at a cause the team can change** (a check, a default, a test), never at "human error": a person
  made the mistake the process let through.

## A weighted trade-off, then how close it is

1. **Write the criteria and their weights (0 to 10) before scoring any option**, each weight with why.
   Weights chosen after the scores bend to the answer someone already wanted.
2. Score each option 1 (worst) to 5 (best) on each criterion, with one line of reason under the table.
3. Run it:

   ```bash
   python ~/.claude/skills/problem-analysis/scripts/tradeoff.py decision.md
   ```

   It prints each option's weighted score, the winner, and how robust the win is: how far each weight would
   have to move before another option wins, and which single score, moved by one point, would change it.
   For a decision that is expensive to undo, the options come from `architect`'s bake-off: three designs
   written apart, scored by a judge who wrote none.
4. **`clear`:** no plausible change flips it. **`close`:** moving one weight by 2 or less, or one score by
   one point, changes the winner. A close table doesn't decide: name the fact that would (a measurement, a
   spike, a question to the people who know) and get it before choosing.

## A pre-mortem

"It's a month after release, and it failed. Why?" Write at least five causes, from different places: the
data, the load, a dependency, the people using it, installed app versions that can't update, the
deployment. Score each likelihood and impact 1 to 3. The top ones get a mitigation, or a check before
release with an owner; the rest are accepted in a line each, so accepting them is a decision.

## Into the ADR

The decision, the options with the table and their scores, and in the reasoning the verdict: for a close
one, the fact that would settle it and how it was got. A worked example:

<!-- sample: tests/problemanalysis/fixture/reminders-delivery.md -->
```markdown
| Criterion | Weight | Broker push | App polls |
|---|---|---|---|
| Reminder on time | 5 | 5 | 3 |
| Works on old app versions | 4 | 2 | 5 |
| Load on the API | 3 | 5 | 2 |
| Ops the team already runs | 2 | 3 | 5 |
```

`tradeoff.py` gives push 77.1% against 72.9%, **close**: with "Reminder on time" weighted 3 instead of 5,
or push scored 1 instead of 2 on old app versions, polling wins. So the ADR says:

```markdown
# Appointment reminders: broker push or app polling

**Date:** 2026-11-15
**Service:** reminders
**Status:** in-progress

## Decision
Push through the broker, once we know how many patients are on app versions without the push SDK.

## Alternatives Considered
- App polls every 15 minutes: 72.9% against push's 77.1% (table above). Works on every installed version,
  but a reminder can be 15 minutes late and the API takes every poll.

## Reasoning
The table is close: polling wins if on-time matters less (weight 3, not 5) or push works on even fewer old
versions. The fact that settles it is the share of active patients on versions without the SDK: under 10%
(from App-Version in the last 30 days of requests), push; otherwise polling until they update.
```

## Estimating in hours

Each task gets three numbers in hours, best, likely and worst, and the unknown that makes the worst worse.
What a task touches comes from the code (`find_callers` on what it changes), not from memory: callers
nobody has read yet are an unknown.

<!-- sample: tests/problemanalysis/fixture/cancel-visit-plan.md -->
```markdown
| Task | Best | Likely | Worst | Unknowns |
|---|---|---|---|---|
| Cancel endpoint with the ownership check | 3 | 4 | 6 | |
| Refund rules (full a day out, half closer) | 2 | 3 | 5 | |
| Free the slot for the waiting list | 2 | 4 | 14 | WaitingList has three callers we haven't read |
| Text message to the patient in their language | 1 | 2 | 4 | |
| Integration tests on SQL Server | 3 | 5 | 8 | |
```

```bash
python ~/.claude/skills/problem-analysis/scripts/pert.py plan.md
```

This plan expects 20 hours, and is done within 22 hours 80% of the time. Promise the 80% figure, not the
sum of the likely hours (18), which comes true less than half the time. `pert.py` also says where the
uncertainty is: here 73% of it is the waiting list, flagged because its worst is over three times its
likely. Spike it (read those callers) before promising anything: with its worst at 6, the 80% figure falls
to 20.

It treats the tasks as independent. When several share one unknown (the same external API, the same
reviewer), they go wrong together: estimate them as one task, or add their worst cases instead.
