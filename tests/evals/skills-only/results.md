# Eval results: the skills-only fixture

## 2026-10-16 — first runs (proven-dotnet 0.2.15)

Held out: nothing in the commands, agents or skills was changed before these runs. One run each, so these
are three results, not rates.

**How it ran.** Each run reviewed a copy of `Appointments` outside the repo, without `evals.md`.
- **With proven:** `/full-review` as it now runs: the known-traps scan first, then `code-reviewer`,
  `security-reviewer` and `dba-reviewer` in parallel, each given the scan's table and told to load its skills.
- **Rules only:** one general-purpose reviewer, no skills, started from the same session (so the house rules
  were in its context).
- **No rules:** `claude -p` from a folder outside the repo with an empty home folder (checked first: it had
  no CLAUDE.md or rules).
- **Grading:** a separate agent that hadn't seen the runs, with the answer key and the three reviews under
  shuffled letters. The with-proven parts were saved with the skill names taken out, so they couldn't be
  told apart by that.

| | With proven | Rules only | No rules |
|---|---|---|---|
| Planted findings (found = 1, partial = 0.5) | **10 / 10** | **9.5 / 10** | **9.5 / 10** |
| Distractors wrongly flagged | 0 / 4 | 0 / 4 | 0 / 4 |
| False claims about the code | 0 | 1 | 0 |

**Where they differed:** only S10. The other two noticed the quorum queue but treated its declaration as
fine; the proven run said RabbitMQ 4 drops the message after its delivery limit when there's no
dead-letter exchange.

**The scan** found 3 of its patterns here (S8, S10, and Hangfire creating its schema at start, real but not
planted). It **missed S9**: the group name was `$"patient-{patientId}"`, and the check looked only for the
bare parameter. The code reviewer caught both the bug and the scan's miss. Fixed after these runs, with the
interpolated form added to the fixture.

## What it means

- **On well-known patterns, a strong model finds nearly everything by itself.** These ten were chosen
  because the always-loaded rules don't mention them, and both runs without the skills found 9.5. Kafka
  offsets, the dual write, an unchecked lock release and a client-named SignalR group are known problems.
- **The extras weren't the skills' either.** All three runs went beyond the key the same way: no unique
  index stops a double booking, a `+03:00` offset fails on `timestamptz`, the SMS shows UTC time. The
  difference was one fact (RabbitMQ 4's delivery limit) and the scan's confirmations, at nearly three times
  the rules-only run's tokens (three reviewers, 378k against 139k).
- **Across three fixtures the pattern holds:** the skills add a little on detection over the rules (+1,
  +0.5, +0.5), and each gain is a tested detail: a CVE pin, a counter read right, a broker's limit. The next
  eval worth running measures the fixes, not the findings: apply each review's fixes and run the tests.
