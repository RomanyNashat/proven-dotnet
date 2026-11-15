# Withdraw a referral

As the referring doctor, I want to withdraw a referral I made, so that the clinic doesn't spend a slot on
a patient who no longer needs it.

- AC1: The referring doctor withdraws an open referral, giving a reason.
- AC2: The clinic no longer sees a withdrawn referral in its queue.
- AC3: The patient is told by text message that the referral was withdrawn.

## Notes from the refinement
- The head of the receiving clinic may withdraw a referral to their clinic too. Nobody else may.
- Withdrawing a referral that is already withdrawn leaves it as it is, and the doctor sees it as withdrawn.
- Accepted and closed referrals can't be withdrawn: the clinic has already acted on them.
