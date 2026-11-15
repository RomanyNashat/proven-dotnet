using Microsoft.Extensions.Options;
using Referrals.Domain;

namespace Referrals.Application;

public enum CreateOutcome
{
    Created,
    AlreadyOpen,
    NotFound,
    TooManyToday,
}

public sealed record CreateResult(CreateOutcome Outcome, Referral? Referral);

public sealed class ReferralDesk(IReferralStore store, IPatientDirectory patients, IOptions<ReferralOptions> options, TimeProvider clock)
{
    public async Task<CreateResult> CreateAsync(int doctorId, int patientId, int clinicId, string reason, string? note, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null || !patient.TreatingDoctorIds.Contains(doctorId))
            return new(CreateOutcome.NotFound, null);

        // Patients over 65 skip the clinic's queue (the clinic policy, 2025).
        var open = await store.FindOpenAsync(patientId, clinicId, ct);
        if (open is not null)
            return new(CreateOutcome.AlreadyOpen, open);

        var today = clock.GetUtcNow().UtcDateTime.Date;
        if (await store.CountMadeSinceAsync(patientId, today, ct) >= options.Value.MaxPerPatientPerDay)
            return new(CreateOutcome.TooManyToday, null);

        var referral = Referral.Make(patientId, doctorId, clinicId, reason, note, clock.GetUtcNow());
        await store.AddAsync(referral, ct);
        return new(CreateOutcome.Created, referral);
    }

    public async Task<bool> AcceptAsync(int referralId, CancellationToken ct)
    {
        var referral = await store.FindAsync(referralId, ct);
        if (referral is null || referral.Status != ReferralStatus.Open)
            return false;
        referral.Accept();
        await store.SaveAsync(referral, ct);
        return true;
    }
}

public sealed record Patient(int Id, IReadOnlyList<int> TreatingDoctorIds, DateOnly BirthDate);

public interface IPatientDirectory
{
    Task<Patient?> FindAsync(int patientId, CancellationToken ct);
}

public interface IReferralStore
{
    Task<Referral?> FindAsync(int id, CancellationToken ct);
    Task<Referral?> FindOpenAsync(int patientId, int clinicId, CancellationToken ct);
    Task<int> CountMadeSinceAsync(int patientId, DateTime sinceUtc, CancellationToken ct);
    Task<IReadOnlyList<Referral>> QueueAsync(int clinicId, CancellationToken ct);
    Task AddAsync(Referral referral, CancellationToken ct);
    Task SaveAsync(Referral referral, CancellationToken ct);
}
