using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet. Every pod runs it, so
/// each reading is claimed before it is sent: the pod whose update wins sends it, the others skip it.</summary>
public sealed class AlertSweeper(IServiceScopeFactory scopes, TimeProvider time)
{
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();

        var now = time.GetUtcNow();
        var due = await db.Readings
            .Where(r => r.Alert != AlertKind.None && r.AlertSentAt == null
                        && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
            .OrderBy(r => r.Id)
            .Take(100)
            .ToListAsync(ct);
        var patientIds = due.Select(r => r.PatientId).Distinct().ToList();
        var phones = await db.Patients
            .Where(p => patientIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Phone, ct);

        var sent = 0;
        foreach (var reading in due)
        {
            // PostgreSQL re-checks the condition after a competing update commits, so only one pod gets 1 here.
            var until = now + ClaimFor;
            var claimed = await db.Readings
                .Where(r => r.Id == reading.Id && r.AlertSentAt == null
                            && (r.AlertClaimedUntil == null || r.AlertClaimedUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertClaimedUntil, until), ct);
            if (claimed == 0)
            {
                continue;
            }

            // If the send fails, the claim runs out and a later run tries again.
            await sms.SendAsync(phones[reading.PatientId], AlertText.For(reading), ct);
            reading.MarkAlertSent(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            sent++;
        }

        return sent;
    }
}
