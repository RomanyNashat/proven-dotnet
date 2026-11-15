namespace Referrals;

public enum CreateOutcome
{
    Created,
    AlreadyOpen,
    NotFound,
    ReasonTooLong,
}

public sealed record CreateResult(CreateOutcome Outcome, Referral? Referral);

public sealed class ReferralDesk(IReferralStore store, IPatientDirectory patients, IClinicInbox inbox, TimeProvider clock)
{
    public const int MaxReasonLength = 500;

    public async Task<CreateResult> CreateAsync(int doctorId, int patientId, int clinicId, string reason, CancellationToken ct)
    {
        if (reason.Length > MaxReasonLength)
            return new(CreateOutcome.ReasonTooLong, null);

        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null || !patient.TreatingDoctorIds.Contains(doctorId))
            return new(CreateOutcome.NotFound, null);

        var open = await store.FindOpenAsync(patientId, clinicId, ct);
        if (open is not null)
            return new(CreateOutcome.AlreadyOpen, open);

        var referral = new Referral
        {
            PatientId = patientId, DoctorId = doctorId, ClinicId = clinicId, Reason = reason,
            CreatedAt = clock.GetUtcNow(),
        };
        await store.AddAsync(referral, ct);

        try
        {
            await inbox.PostAsync(clinicId, referral.Id, ct);
            referral.MarkClinicNotified();
        }
        catch (HttpRequestException)
        {
            // The clinic's inbox is down: the referral stands, and the outbox job posts it later.
        }
        await store.SaveAsync(referral, ct);
        return new(CreateOutcome.Created, referral);
    }
}
