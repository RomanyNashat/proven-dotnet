using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

/// <summary>A reading as the app sends it. <c>Value</c> is the text the meter shows.</summary>
public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>A saved reading, or why it was refused.</summary>
public sealed record AddReadingResult(Reading? Reading, IDictionary<string, string[]>? Errors)
{
    public static AddReadingResult Saved(Reading reading) => new(reading, null);

    public static AddReadingResult Invalid(string field, string message) => new(null, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    /// <summary>Saves a reading validated by <see cref="NewReadingValidator"/>, with its alert in the same transaction.</summary>
    public async Task<AddReadingResult> AddAsync(NewReading request, CancellationToken ct)
    {
        if (!GlucoseUnit.TryNormalize(request.Unit, out var unit)
            || !MeterValue.TryParse(request.Value, unit, out var value)
            || !NationalId.TryNormalize(request.NationalId, out var nationalId))
        {
            throw new ArgumentException("The reading must be validated before it is added.", nameof(request));
        }

        // The national ID comes from the body, and the alert a nurse acts on carries it. Until it can come
        // from the identity provider, a patient's readings all carry the ID of their first one.
        var patientId = patient.Id;
        var known = await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.Id)
            .Select(r => r.NationalId)
            .FirstOrDefaultAsync(ct);
        if (known is not null && known != nationalId)
        {
            return AddReadingResult.Invalid("nationalId", "nationalId does not match this patient's earlier readings.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reading = Reading.Record(patientId, nationalId, value, unit, ReadingTime.ToUtc(request.CollectedAt), request.Notes, now);
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Reading {ReadingId} received; care team alert: {NeedsAlert}", reading.Id, reading.NeedsAlert);
        return AddReadingResult.Saved(reading);
    }

    /// <summary>Stores a photo checked by <see cref="PhotoStore.Check"/> on one of the caller's readings.</summary>
    /// <returns>False when the reading doesn't exist or isn't the caller's.</returns>
    public async Task<bool> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var patientId = patient.Id;
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patientId, ct);
        if (reading is null)
        {
            return false;
        }

        var key = await photos.SaveAsync(readingId, file, ct);
        try
        {
            reading.AttachPhoto(key);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            photos.Delete(key);   // don't leave a patient's photo on disk that no reading points to
            throw;
        }

        return true;
    }
}
