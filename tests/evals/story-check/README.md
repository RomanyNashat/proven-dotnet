# The story-check eval: does it ask the right questions, and only those?

A run gets a small referrals service (`Referrals/src/`) and two stories about to be built in it
(`Referrals/stories/refer.md`, `withdraw.md`), and writes, for each story, which cases are still open
(`task.md`). Both arms get the same task, the same nine case names and the same report shape; the harness
arm is also sent to the `story-check` skill and its script (`route.txt`). A run never sees the key.

**How a copy is graded** (`tools/grade_one.py`, against `key.json`):
- **G1–G4, the planted gaps:** pass when the report marks that case a gap.
- **A1–A7, the hidden answers:** pass when it doesn't. Each is settled where a careful reader finds it: in
  the code (A1–A3) or in the stories' notes (A4–A7).
- The cases not in the key are not graded: either answer is defensible there.

| Key | Story, case | Why |
|---|---|---|
| G1 | refer, empty | no mobile number on file: AC3's text message has nowhere to go |
| G2 | withdraw, failure | the clinic's inbox down while withdrawing |
| G3 | withdraw, language | AC3's message has no language (only the other story's notes give one) |
| G4 | withdraw, touches | `WaitingList` counts every referral that isn't Closed, so a withdrawn one still lengthens the wait |
| A1 | refer, maximum | `ReferralDesk` refuses a reason over 500 characters |
| A2 | refer, permission | `ReferralDesk` answers NotFound unless the doctor treats the patient |
| A3 | refer, duplicates | `ReferralDesk` returns the open referral to the same clinic |
| A4 | refer, existing-data | the notes: paper referrals stay on paper |
| A5 | refer, language | the notes: the profile's language |
| A6 | withdraw, permission | the notes: the referring doctor and the clinic's head, nobody else |
| A7 | withdraw, duplicates | the notes: withdrawing twice leaves it as it is |

`summary.json` also reports, ungraded: the questions per story, any asked about a case the key doesn't
grade, and whether the skill's own script accepts each report.

The copies:

| Copy | Gaps asked | Answers not asked |
|---|---|---|
| `original` (no report) | 0 / 4 | 7 / 7 |
| `reference` (the key; the script accepts both reports) | 4 / 4 | 7 / 7 |

## The bar, written before any run (2026-11-15)

- **Arms:** rules only (`core.md` and the rules) and the harness (`route.txt`: the `story-check` skill and its
  script).
- **Runs:** `claude-opus-5-5` at medium, three per arm, after a pilot.
- **Pilot first:** one rules-only run. If it asks all four gaps and none of the seven answers, the fixture
  can't tell the arms apart: redesign it before running the rest.
- **Win**, averaged over the runs, and larger than the noise floor (two identical harness arms, measured
  once): the harness asks at least 1.5 fewer settled cases (A failures) per run with no fewer gaps found,
  or finds at least 1 more gap per run with no more settled cases asked.
- **Falsifier:** neither. Then the skill's routine and script don't earn their cost on stories like these,
  and that is the result.
- **Also reported:** cost, time, the questions asked outside the key, and how often the script accepts the
  rules-only arm's reports (it never sees the script).

## The pilot (2026-11-15): the fixture can't tell the arms apart

One rules-only run (`claude-opus-5-5`, medium, this branch's first commit): $0.49, 14 turns, 106 seconds.
Its reports are in `runs/rules-1`.

- **It asked all four gaps and none of the seven answers**: the pilot rule's "redesign" case. It cited the
  code for the three answers there, quoted the notes for the four in the stories, and the skill's script,
  which this arm never saw, accepted both reports.
- **Its five questions outside the key are fair ones**, not noise: the text-message service down, a stop
  between saving and texting, in both stories (at least once or at most once), the withdrawal reason's length, a patient
  with no mobile when withdrawing, and three more readers of a referral's status under `touches`
  (`FindOpenAsync`, the outbox, `Close()`).

**What it means, and why there is no redesign.** The task already gives both arms the nine cases, the
report shape and "don't ask what is settled": what is left for the skill to add is its rules and its
script, and a strong model does without them here. A redesign would be written after reading this run's
answers, so it would be tuned to make the baseline miss, and a win on it wouldn't mean much (the same
reason `fix-qa` got no third design). So the grid wasn't run.

The skill stays for what it enforces rather than for what it finds: one routine every time, every settled
case backed by a quote or a line, and a script that refuses a made-up quote, a line that doesn't hold its
claim, or a question the story answers, before anyone reads the report. That holds on a smaller or
faster model, or on a bad day, where nothing here measured it.

**What would measure more:** a plain request with no case list ("is this story ready to build?") against
the harness, graded by a judge that maps each question to a case. It measures what a developer gets
without the harness, but needs a judge this repo doesn't have yet, so it is parked rather than built.
