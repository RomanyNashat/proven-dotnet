using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time)
{
    private const long SweepLock = 0x4D53_5357;   // "MSSW": this service's sweep

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        // Every pod runs this loop: one sweeps, the others skip this run. The lock is the transaction's, so a
        // pod that dies mid-sweep releases it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var mine = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({SweepLock}) AS \"Value\"").SingleAsync(ct);
        if (!mine)
        {
            return 0;
        }

        var due = await db.Readings
            .Where(r => r.Alert != AlertKind.None && r.AlertSentAt == null)
            .OrderBy(r => r.Id)
            .Take(100)
            .ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        foreach (var reading in due)
        {
            await sms.SendAsync(phones[reading.PatientId], AlertText.For(reading), ct);
            reading.MarkAlertSent(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return due.Count;
    }
}
