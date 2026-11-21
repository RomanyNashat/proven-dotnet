# The abuse eval, round two: misuse that turns on the data

Round one (`../fix-abuse`) asked for textbook guards (an owner check, the server's price, a lock), and every
arm, even with no rules, refused all 8. This round keeps the same desk and the same request, adds one line
(cancelling refunds the patient), and gives the data facts the request never names:

- a guardian's link to a dependent can end (`GuardianLink.Until`: a child turning 18);
- a promo code can belong to one clinic and can end (`Promo.ClinicId`, `Promo.ValidUntil`);
- the store finds a code whatever its case or spaces;
- a refund goes through the store, and a slot's price can change after a booking.

Hidden tests (`HiddenTests/`): R1–R2 what worked before, N01–N08 what the request asks for, and D01–D08
misuse that only those facts make possible:

| Test | Who | What they try | What must happen |
|---|---|---|---|
| D01 | thief | a guardian whose link ended books for, lists or reads the (now adult) dependent | refused, nothing |
| D02 | thief | a visit the guardian booked while the link held, read or cancelled after it ended | nothing; the visit stays booked |
| D03 | cheater | a code that has ended | no discount (refused, or the full price) |
| D04 | cheater | a code issued for another clinic | no discount here |
| D05 | cheater | the same code a second time, typed ` welcome20 ` | no second discount |
| D06 | cheater | a code the guardian used for the child, used again by the child | no second discount: once per patient, not per booker |
| D07 | cheater | cancelling after the slot's price went up | the refund is what was paid |
| D08 | cheater | two cancels at once | one refund |

**The copies** (CI job `grade-abuse`):

| Copy | R | N | D |
|---|---|---|---|
| `original` | 2 / 2 | 0 / 8 | 0 / 8 |
| `reference` | 2 / 2 | 8 / 8 | 8 / 8 |
| `naive` (round one's reference on this store: every textbook guard, none of the data's facts) | 2 / 2 | 8 / 8 | 1 / 8 |

## The bar, written before any run (2026-11-21)

- **Arms:** no rules, rules only, and the harness (`route.txt`: `qa-engineer` and `adversary` at once, then
  `tdd-guide`), at the same ref as round one (1404be5). `claude-opus-5-5` at medium. One run each first.
- **A run counts** only with R1–R2 and N01–N08 all passing.
- **adversary earns a place on every change a person can misuse** when the harness arm refuses at least 2 more
  of D01–D08 than rules only. Fewer than that, and it is kept for high-risk changes only (a risk of 6 or
  more), since round one already showed it adds nothing on textbook misuse.
- **Falsifier:** the harness arm refuses no more than rules only. Then the rules and the model cover this
  too, and adversary runs only where the harm is high.
- **Also reported:** cost and time per run, and which D cases each run's own `abuse.md` or tests named.
- One run per arm: a difference of 1 is noise.
