using Microsoft.EntityFrameworkCore;
using Referrals.Application;
using Referrals.Domain;

namespace Referrals.Infrastructure;

public sealed class ReferralRepository(ReferralsDb db, TimeProvider clock) : IReferralStore
{
    private static readonly TimeSpan QueueWindow = TimeSpan.FromDays(90);

    public Task<Referral?> FindAsync(int id, CancellationToken ct) => db.Referrals.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Referral?> FindOpenAsync(int patientId, int clinicId, CancellationToken ct) =>
        db.Referrals.FirstOrDefaultAsync(r => r.PatientId == patientId && r.ClinicId == clinicId && r.Status == ReferralStatus.Open, ct);

    public Task<int> CountMadeSinceAsync(int patientId, DateTime sinceUtc, CancellationToken ct) =>
        db.Referrals.CountAsync(r => r.PatientId == patientId && r.CreatedAt >= sinceUtc, ct);

    public async Task<IReadOnlyList<Referral>> QueueAsync(int clinicId, CancellationToken ct)
    {
        var from = clock.GetUtcNow() - QueueWindow;
        return await db.Referrals.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.Status == ReferralStatus.Open && r.CreatedAt >= from)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Referral referral, CancellationToken ct)
    {
        db.Referrals.Add(referral);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(Referral referral, CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class ReferralsDb(DbContextOptions<ReferralsDb> options) : DbContext(options)
{
    public DbSet<Referral> Referrals => Set<Referral>();
}
