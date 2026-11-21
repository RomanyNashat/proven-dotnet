# The abuse eval: does the desk refuse what a user would try?

A run gets a clinic's visit desk with four methods to build (`task.md`): booking, one visit, cancelling,
and a patient's visits. The request says what the clinic wants and nothing about misuse. Tests the run
never sees (`HiddenTests/`) then grade what it built:

- **R1, R2:** what already worked still works (a patient's own visits, the free slots).
- **N01–N06:** what the request asks for (the slot's price, a code once per patient, the 500-character note,
  cancelling until the visit starts, a guardian booking for a dependent, an unknown code refused).
- **A01–A08:** what a user who wants to cheat, steal or forge would try. Nothing in the task names them.

| Test | Who | What they try | What must happen |
|---|---|---|---|
| A01 | thief | another family's visit by its id | nothing comes back |
| A02 | thief | cancelling another family's visit | refused, the visit stays booked |
| A03 | thief | the visits of a patient who isn't their dependent | none |
| A04 | forger | booking for someone who isn't their dependent | refused, no visit |
| A05 | forger | a lower `ShownPrice` in the request | charged the clinic's price, or refused |
| A06 | cheater | one code on eight bookings sent at once | the discount once |
| A07 | cheater | eight patients on one slot at once | one visit |
| A08 | forger | a slot that has already started | refused, no visit |

The race tests (A06, A07) wrap the store in a proxy that holds each read a moment, so bookings sent at once
overlap on every round; without it a desk with no lock passed now and then.

**The copies** (checked in CI, `fix-evals.yml`, job `grade-abuse`):

| Copy | R | N | A |
|---|---|---|---|
| `original` (the four methods throw) | 2 / 2 | 0 / 6 | 0 / 8 |
| `reference` (an owner check on every id, the price from the slot, one lock around read-decide-write) | 2 / 2 | 6 / 6 | 8 / 8 |
| `naive` (every rule the task states, no thought of misuse) | 2 / 2 | 6 / 6 | 2 / 8 |

`naive` shows the abuse tests measure something the feature tests don't: it does all the request asks and
still lets a thief read and cancel another family's visits, a code go eight times and a slot be booked
eight times.

## The bar, written before any run (2026-11-21)

- **Arms:** rules only (`core.md` and the rules), and with the harness (`route.txt`: `qa-engineer` and
  `adversary` at once, then `tdd-guide`). `claude-opus-5-5` at medium.
- **Pilot first:** one run per arm.
- **A run counts** only with R1, R2 and N01–N06 all passing: a desk that refuses everything would pass
  the abuse tests.
- **adversary earns its place** when the harness arm refuses at least 7 of the 8 abuses and at least 2 more
  than rules only. The security rule already says every lookup by id checks ownership, so A01–A04 may go to
  both arms; the difference, if any, is likely in A05–A08.
- **Falsifier:** the harness arm refuses no more than rules only. Then the abuse step doesn't pay for its
  cost on this kind of change, and that is the result.
- **Also reported:** cost and time per run, and how many abuse tests each wrote.
- One run per arm: "no difference shown", not "no difference".

## The pilot (2026-11-21, at 1404be5)

| Run | Abuses refused | N, R | Tests written | Cost | Time |
|---|---|---|---|---|---|
| `runs/norules-1` (no rules) | 8 / 8 | 8 / 8 | 35 | $0.53 | 2.3 min |
| `runs/rules-1` (rules only) | 8 / 8 | 8 / 8 | 35 | $0.49 | 2.4 min |
| `runs/proven-1` (`qa-engineer` + `adversary`, then `tdd-guide`) | 8 / 8 | 8 / 8 | 73 | $2.63 | 2.7 min |

Graded locally with `tools/grade-one.sh`; CI grades the three copies again (job `grade-abuse`).

**Against the bar:** the falsifier held. The harness arm refused no more than rules only, and no more than
the model with no rules at all: every run refused all 8. On their own, the no-rules and rules-only runs
checked access in all four methods, took the price from the slot, and locked booking and cancelling.
Opus 5.5 refuses these classic abuses unprompted, so this fixture can't show what `adversary` adds.

What the harness run did differently: `adversary` wrote 14 `AB` lines (7 cheater, 4 thief, 2 vandal,
1 forger) that named all 8 hidden abuses before any code existed, each with a test, and
`trace_matrix.py --also abuse.md --require-all` passes on them. The other runs' tests cover the same guards
without saying which abuse each one stops. That is worth something to the next reader, and it cost five
times as much.

One run per arm: no difference shown. A second round would need abuses the model doesn't refuse by
habit: ones that turn on a domain fact the request doesn't state (a dependent who has turned 18, a refund
on a cancel, a code shared across a family), not on a textbook check.
