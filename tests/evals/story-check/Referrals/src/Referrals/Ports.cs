namespace Referrals;

public sealed record Patient(int Id, IReadOnlyList<int> TreatingDoctorIds, string? Mobile, string Language);

public interface IPatientDirectory
{
    Task<Patient?> FindAsync(int patientId, CancellationToken ct);
}

public interface IReferralStore
{
    Task<Referral?> FindOpenAsync(int patientId, int clinicId, CancellationToken ct);
    Task<int> CountAsync(int clinicId, Func<Referral, bool> where, CancellationToken ct);
    Task AddAsync(Referral referral, CancellationToken ct);
    Task SaveAsync(Referral referral, CancellationToken ct);
}

public interface IClinicInbox
{
    Task PostAsync(int clinicId, int referralId, CancellationToken ct);
}
