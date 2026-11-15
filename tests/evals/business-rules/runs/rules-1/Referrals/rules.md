# Referrals: business rules as enforced today

I worked these out by reading the code. The snapshot has no `.csproj` files, so I couldn't build or run it.

**Check the wiring first.** `Program.cs` registers neither `ReferralsDb` (no `AddDbContext`) nor any
`IPatientDirectory` (no implementation exists anywhere in `src/`). As written, the container can't build
`ReferralRepository` or `ReferralDesk`, so every endpoint fails with a 500. In Development, `app.Build()`
fails at start-up instead. The rules below describe what the code does once those two pieces are supplied.
If the deployed service runs, its real composition root lives outside this folder. Diff it against this
one before the refactor.

## Enforced

R1: Given a create request, when `patientId`, `clinicId` or `reason` is missing from the JSON body, then it is rejected with 400 (C# `required` members fail deserialization; `[ApiController]` returns the error) (CreateReferralRequest.cs:8, CreateReferralRequest.cs:11, CreateReferralRequest.cs:14)
R2: Given a create request, when `reason` is null, empty or only whitespace, then it is rejected with 400 (CreateReferralRequest.cs:13)
R3: Given a create request, when `reason` is longer than 500 characters, then it is rejected with 400 (CreateReferralRequest.cs:13)
R4: Given a create request, when `note` is missing or empty, then it is accepted, because the note is optional (CreateReferralRequest.cs:16)
R5: Given a create request, when the patient doesn't exist or the `X-Doctor-Id` header doesn't name one of the patient's treating doctors, then no referral is made and the response is 404. A missing header binds as doctor 0. (ReferralDesk.cs:21, ReferralsController.cs:19)
R6: Given the patient already has an Open referral to the same clinic, when another referral to that clinic is requested, then nothing is created and the existing referral comes back with 200. Only Open counts: after an Accepted or Closed referral, a new one can be made. This check runs before R7, so a duplicate still gets 200 when the daily cap is reached. (ReferralDesk.cs:26, ReferralsController.cs:17)
R7: Given the patient has had `MaxPerPatientPerDay` referrals (configured as 3) created since 00:00 UTC today, counting every doctor, clinic and status, when another one is requested, then it is refused with 429 (ReferralDesk.cs:30, appsettings.json:3)
R8: Given a create request passes R1–R7, when it is processed, then a referral is stored as Open, stamped with the current UTC time, and returned with 201 and `Location: /referrals/{id}` (Referral.cs:24, ReferralDesk.cs:33, ReferralsController.cs:16)
R9: Given an Open referral, when `POST /referrals/{id}/accept` is called, then it becomes Accepted and the response is 204 (ReferralDesk.cs:43, Referral.cs:30, ReferralsController.cs:25)
R10: Given a referral that doesn't exist, or is Accepted or Closed, when accept is called, then nothing changes and the response is 409. An unknown id gets 409, not 404. (ReferralDesk.cs:41, ReferralsController.cs:25)
R11: Given a referral that isn't Open, when the domain is asked to accept it, then it throws `InvalidOperationException` and the status doesn't change. Through the API this only fires if a race gets past R10. (Referral.cs:28)
R12: Given a clinic, when its queue is requested, then the response lists only that clinic's Open referrals created in the last 90 days, oldest first (ReferralRepository.cs:23, ReferralRepository.cs:24, ReferralRepository.cs:9)
R13: Given an Open referral older than 90 days, when the queue is read, then it is left out, but it stays Open: it still blocks duplicates under R6 and can still be accepted under R9 (ReferralRepository.cs:21, ReferralRepository.cs:14)

R6 and R7 check and then insert in separate steps, and no unique index or lock is visible. Two requests
that arrive together can both pass, which gives a duplicate Open referral or a fourth referral in one day.

## Not enforced

N1: Referrals expire after 30 days if not accepted (README.md:7). Nothing expires or closes a referral: no job calls `Referral.Close()`, and no query checks a 30-day age. The only time window is the queue's 90 days (R12, R13).
N2: Patients over 65 skip the clinic's queue (ReferralDesk.cs:24, comment only). No code reads `Patient.BirthDate`, and the queue is ordered by `CreatedAt` alone (ReferralRepository.cs:24).
N3: Each doctor may make at most 5 referrals a day (`MaxDailyReferrals`, ReferralOptions.cs:10). Nothing reads the constant. The only cap is per patient (R7).
N4: `MaxPerPatientPerDay` must be between 1 and 20 (ReferralOptions.cs:12). `ValidateOnStart()` is there, but `ValidateDataAnnotations()` isn't (Program.cs:7), so the `[Range]` never runs. If the setting is missing it becomes 0, and every create returns 429.
N5: Every referral needs a note (CreateReferralValidator.cs:10). The FluentValidation validator is never registered or called, so the note stays optional (R4).
N6: The reason must be at least 10 characters (CreateReferralValidator.cs:11). The same validator is never run, so a 1-character reason passes.
N7: The visit fee is 150, with 10% off from the patient's 5th visit of the year (FeeCalculator.cs:8). Nothing calls `FeeCalculator`, and no endpoint charges a fee.
N8: Referrals can be Closed (Referral.cs:7, Referral.cs:33). `Close()` has no caller, so a referral can't reach Closed while the service runs.
N9: Only the clinic accepts its own referrals (README.md:5). The service has no authentication or authorization: any caller can accept any referral id (ReferralsController.cs:23) and read any clinic's queue (ClinicsController.cs:10). The doctor's identity is whatever the caller puts in `X-Doctor-Id` (ReferralsController.cs:11), so R5 is only as strong as that header. Patient data can leak this way (IDOR).
