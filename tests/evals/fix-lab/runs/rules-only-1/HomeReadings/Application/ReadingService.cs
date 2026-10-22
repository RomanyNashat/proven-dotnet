using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeReadings.Application;

/// <param name="Id">The reading's id.</param>
/// <param name="Created">False when the same reading had already been received (the app retried).</param>
public sealed record AddReadingResult(int Id, bool Created);

public enum AttachPhotoResult
{
    Attached,
    NotFound,
}

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    public async Task<AddReadingResult> AddAsync(ValidReading request, CancellationToken ct)
    {
        var patientId = patient.Id;
        if (await FindSameAsync(patientId, request, ct) is { } existingId)
        {
            logger.LogInformation("Reading {ReadingId} received again; not stored twice", existingId);
            return new AddReadingResult(existingId, false);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patientId, request.NationalId, request.Value, request.Unit, request.CollectedAtUtc, request.Notes, now);

        try
        {
            // The reading and its alert commit together: a high reading must never be saved without one.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Readings.Add(reading);
            await db.SaveChangesAsync(ct);
            if (reading.NeedsAlert)
            {
                db.Alerts.Add(new CareTeamAlert(reading.Id, now));
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same reading arrived twice at the same moment and the other request stored it.
            db.ChangeTracker.Clear();
            if (await FindSameAsync(patientId, request, ct) is { } raceId)
            {
                return new AddReadingResult(raceId, false);
            }

            throw;
        }

        // Ids only: the reading holds a national ID and health data, which stay out of the logs.
        logger.LogInformation("Reading {ReadingId} received, alert {NeedsAlert}", reading.Id, reading.NeedsAlert);
        return new AddReadingResult(reading.Id, true);
    }

    public async Task<AttachPhotoResult> AttachPhotoAsync(int readingId, IFormFile file, string extension, CancellationToken ct)
    {
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patient.Id, ct);
        if (reading is null)
        {
            return AttachPhotoResult.NotFound;
        }

        var previous = reading.PhotoPath;
        var path = await photos.SaveAsync(readingId, file, extension, ct);
        reading.AttachPhoto(path);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            photos.Delete(path);
            throw;
        }

        photos.Delete(previous);
        return AttachPhotoResult.Attached;
    }

    private async Task<int?> FindSameAsync(int patientId, ValidReading request, CancellationToken ct) =>
        await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.CollectedAt == request.CollectedAtUtc
                && r.Value == request.Value && r.Unit == request.Unit)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
}
