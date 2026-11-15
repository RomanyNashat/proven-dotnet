# Rules: Referrals

- R1: Given a doctor who doesn't treat the patient (or a patient who doesn't exist), when they refer the patient, then the answer is NotFound. (ReferralDesk.cs:21 `!patient.TreatingDoctorIds.Contains(doctorId)`)
- R2: Given a referral to a clinic that is still open, when the doctor refers the patient to that clinic again, then the open referral comes back (200) and no second one is made. (ReferralDesk.cs:26 `if (open is not null)`)
- R3: Given a referral request, when its reason is missing or longer than 500 characters, then the API answers 400; the patient and the clinic are required too. (CreateReferralRequest.cs:13 `[Required, MaxLength(500)]`) (CreateReferralRequest.cs:7 `[Required]`) (CreateReferralRequest.cs:10 `[Required]`)
- R4: Given a referral that isn't open (or doesn't exist), when the clinic accepts it, then the answer is 409 and nothing changes; an open one becomes Accepted. (ReferralDesk.cs:41 `referral.Status != ReferralStatus.Open`) (Referral.cs:28 `if (Status != ReferralStatus.Open)`) (Referral.cs:29 `throw new InvalidOperationException`) (Referral.cs:30 `Status = ReferralStatus.Accepted`)
- R5: Given a clinic's queue, when it is read, then it shows only open referrals made in the last 90 days, oldest first. (ReferralRepository.cs:23 `r.Status == ReferralStatus.Open && r.CreatedAt >= from`) (ReferralRepository.cs:9 `TimeSpan.FromDays(90)`)
- R6: Given a patient who already has MaxPerPatientPerDay referrals (3 in appsettings, between 1 and 20) since UTC midnight, when another is made, then the answer is 429. (ReferralDesk.cs:30 `>= options.Value.MaxPerPatientPerDay`) (ReferralOptions.cs:12 `[Range(1, 20)]`)
- R7: Given a new referral, when it is made, then it starts Open. (Referral.cs:24 `Status = ReferralStatus.Open`)

## Questions the rules raise
- R6 counts from UTC midnight, not the clinic's: at UTC+3, a referral made at 01:00 local time counts toward the day before.

## Not rules
- CreateReferralValidator.cs:10 `RuleFor(r => r.Note)`: never registered (no AddValidators and no FluentValidation integration), so a note isn't required.
- CreateReferralValidator.cs:11 `RuleFor(r => r.Reason).MinimumLength(10)`: the same validator, never run.
- ReferralOptions.cs:10 `MaxDailyReferrals = 5`: never used; the cap is MaxPerPatientPerDay.
- FeeCalculator.cs:6 `BaseFee = 150m`: FeeCalculator is never called.
- FeeCalculator.cs:9 `Math.Round(BaseFee * 0.9m, 2)`: never called, so there is no discount.
- Referral.cs:33 `Status = ReferralStatus.Closed`: Close() is never called.

## Not enforced
- "Patients over 65 skip the clinic's queue" (ReferralDesk.cs, a comment): no code does it.
- "A referral expires after 30 days" (README): no code does it.
