using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Application;

/// <summary>Texts the patient about every reading out of range that hasn't been sent yet.</summary>
/// <remarks>
/// Safe on several pods: each reading is claimed with <c>FOR UPDATE SKIP LOCKED</c> inside its own transaction,
/// so two sweepers never text the same reading at once. A reading that keeps failing is retried
/// <see cref="MaxFailures"/> times and then left alone, so it can't block the others.
/// </remarks>
public sealed class AlertSweeper(SyncDbContext db, SmsClient sms, TimeProvider time, ILogger<AlertSweeper> logger)
{
    /// <summary>Attempts after which a reading is no longer texted.</summary>
    public const int MaxFailures = 5;

    private const int BatchSize = 100;

    private enum Outcome
    {
        NoneDue,
        Sent,
        Failed,
    }

    /// <summary>Texts up to 100 due readings, one transaction each.</summary>
    /// <param name="ct">Stops the run.</param>
    /// <returns>The number of SMS sent.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var tried = new List<int>();
        var sent = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            var outcome = await SendNextAsync(tried, ct);
            if (outcome == Outcome.NoneDue)
            {
                break;
            }

            if (outcome == Outcome.Sent)
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<Outcome> SendNextAsync(List<int> tried, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var skip = tried.ToArray();

        // Not composed with LINQ: EF would wrap the query in a subquery and the row lock would not apply as written.
        var claimed = await db.Readings.FromSql($"""
            SELECT * FROM readings
            WHERE "Alert" <> 'None' AND "AlertSentAt" IS NULL AND "AlertFailures" < {MaxFailures}
              AND NOT ("Id" = ANY({skip}))
            ORDER BY "Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        if (claimed is not [var reading])
        {
            return Outcome.NoneDue;
        }

        string failure;
        try
        {
            var phone = await db.Patients.Where(p => p.Id == reading.PatientId).Select(p => p.Phone).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(phone))
            {
                await sms.SendAsync(phone, AlertText.For(reading), ct);
                reading.MarkAlertSent(time.GetUtcNow());

                // A crash between the gateway accepting and this commit resends once: a duplicate beats a lost hypo/hyper alert.
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Outcome.Sent;
            }

            failure = "MissingPhone";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex.GetType().Name;
        }

        // Disposing rolls back and, unlike RollbackAsync, doesn't throw when a failed commit already ended it.
        await tx.DisposeAsync();
        await RecordFailureAsync(reading.Id, failure, tried, ct);
        return Outcome.Failed;
    }

    private async Task RecordFailureAsync(int readingId, string failure, List<int> tried, CancellationToken ct)
    {
        tried.Add(readingId);
        db.ChangeTracker.Clear();
        await db.Readings
            .Where(r => r.Id == readingId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AlertFailures, r => r.AlertFailures + 1), ct);

        // Never the phone or the text: both are personal data.
        logger.LogWarning("Alert for reading {ReadingId} failed ({ErrorType}); will retry up to {MaxFailures} times", readingId, failure, MaxFailures);
    }
}
