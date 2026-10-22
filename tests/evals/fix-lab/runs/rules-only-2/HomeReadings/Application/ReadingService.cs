using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeReadings.Application;

public enum AddReadingOutcome
{
    Created,

    /// <summary>The same reading was already saved (the app sent it again); nothing new was stored.</summary>
    AlreadySaved,
    Invalid,
}

public sealed record AddReadingResult(AddReadingOutcome Outcome, Reading? Reading, IDictionary<string, string[]> Errors);

public enum AttachPhotoOutcome
{
    Attached,
    NotFound,
    NotAnImage,
}

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    public async Task<AddReadingResult> AddAsync(NewReading request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var valid = NewReadingValidator.Validate(request, now, out var errors);
        if (valid is null)
        {
            return new AddReadingResult(AddReadingOutcome.Invalid, null, errors);
        }

        var patientId = patient.Id;
        if (await FindSameAsync(patientId, valid, ct) is { } existing)
        {
            return new AddReadingResult(AddReadingOutcome.AlreadySaved, existing, errors);
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
            // The same reading arrived twice at once; the other request saved it.
            db.ChangeTracker.Clear();
            if (await FindSameAsync(patientId, valid, ct) is { } saved)
            {
                return new AddReadingResult(AddReadingOutcome.AlreadySaved, saved, errors);
            }

            throw;
        }

        logger.LogInformation("Reading {ReadingId} saved for patient {PatientId}; alert: {NeedsAlert}", reading.Id, patientId, reading.NeedsAlert);
        return new AddReadingResult(AddReadingOutcome.Created, reading, errors);
    }

    public async Task<AttachPhotoOutcome> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patient.Id, ct);
        if (reading is null)
        {
            return AttachPhotoOutcome.NotFound;
        }

        var saved = await photos.SaveAsync(readingId, file, ct);
        if (saved is null)
        {
            return AttachPhotoOutcome.NotAnImage;
        }

        var previous = reading.PhotoPath;
        reading.AttachPhoto(saved);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            photos.Delete(saved);
            throw;
        }

        if (previous is not null)
        {
            photos.Delete(previous);
        }

        return AttachPhotoOutcome.Attached;
    }

    private Task<Reading?> FindSameAsync(int patientId, ValidReading valid, CancellationToken ct) =>
        db.Readings.AsNoTracking().SingleOrDefaultAsync(
            r => r.PatientId == patientId && r.CollectedAt == valid.CollectedAtUtc && r.Value == valid.Value && r.Unit == valid.Unit,
            ct);
}
