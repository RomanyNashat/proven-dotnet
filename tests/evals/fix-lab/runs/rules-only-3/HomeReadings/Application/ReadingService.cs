using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed record AddReadingResult(Reading? Reading, Dictionary<string, string[]> Errors);

public enum PhotoResult
{
    Saved,
    NotFound,
    NotAnImage,
}

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    public async Task<AddReadingResult> AddAsync(NewReading request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (NewReadingValidator.Validate(request, now, out var errors) is not { } valid)
        {
            return new AddReadingResult(null, errors);
        }

        var patientId = patient.Id;
        if (await FindSameAsync(patientId, valid, ct) is { } existing)
        {
            logger.LogInformation("Reading {ReadingId} was sent again; returning the stored one", existing.Id);
            return new AddReadingResult(existing, errors);
        }

        var reading = new Reading(patientId, valid.NationalId, valid.Value, valid.Unit, valid.CollectedAtUtc, valid.Notes, now);
        db.Readings.Add(reading);
        if (reading.NeedsAlert)
        {
            // Saved with the reading in one transaction: a high reading is never stored without its alert.
            db.Alerts.Add(new CareTeamAlert(reading, now));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same reading arrived twice at once (an app retry racing the first request).
            db.ChangeTracker.Clear();
            var stored = await FindSameAsync(patientId, valid, ct)
                ?? throw new InvalidOperationException("A unique violation was raised but the reading is not stored.", ex);
            return new AddReadingResult(stored, errors);
        }

        // No national ID, value or notes in logs: they are patient data.
        logger.LogInformation("Reading {ReadingId} received for patient {PatientId}; care team alert: {NeedsAlert}",
            reading.Id, patientId, reading.NeedsAlert);
        return new AddReadingResult(reading, errors);
    }

    public async Task<PhotoResult> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patient.Id, ct);
        if (reading is null)
        {
            return PhotoResult.NotFound;
        }

        if (await PhotoStore.DetectImageTypeAsync(file, ct) is not { } extension)
        {
            return PhotoResult.NotAnImage;
        }

        reading.AttachPhoto(await photos.SaveAsync(readingId, file, extension, ct));
        await db.SaveChangesAsync(ct);
        return PhotoResult.Saved;
    }

    private Task<Reading?> FindSameAsync(int patientId, ValidReading valid, CancellationToken ct) =>
        db.Readings.AsNoTracking().SingleOrDefaultAsync(
            r => r.PatientId == patientId && r.CollectedAt == valid.CollectedAtUtc && r.Value == valid.Value, ct);
}
