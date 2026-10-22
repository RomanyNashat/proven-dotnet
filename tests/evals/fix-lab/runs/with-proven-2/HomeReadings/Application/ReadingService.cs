using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class ReadingService(
    ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, IMemoryCache cache, ILogger<ReadingService> logger)
{
    /// <summary>Saves a reading validated by <see cref="NewReadingValidator"/>, and its care-team alert when it is high.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="ct">Cancels the save; the reading and its alert commit together or not at all.</param>
    /// <returns>The saved reading, or a national-ID mismatch.</returns>
    /// <exception cref="ArgumentException">The request was not validated first.</exception>
    public async Task<AddReadingResult> AddAsync(NewReading request, CancellationToken ct)
    {
        if (!MeterValue.TryParse(request.Value, out var value) || !GlucoseUnits.TryNormalize(request.Unit, out var unit))
        {
            throw new ArgumentException("The reading was not validated.", nameof(request));
        }

        var patientId = patient.Id;
        // The national ID still comes from the body (the app sends it) and goes to the care team, so a
        // patient's later readings must carry the same one as their first.
        if (await db.Readings.AnyAsync(r => r.PatientId == patientId && r.NationalId != request.NationalId, ct))
        {
            return AddReadingResult.NationalIdMismatch;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patientId, request.NationalId, value, unit, RiyadhTime.ToUtc(request.CollectedAt), request.Notes, now);

        // One transaction: a high reading saved without its alert is a nurse who is never told.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.Readings.Add(reading);
            await db.SaveChangesAsync(ct);
            if (reading.NeedsAlert)
            {
                db.Alerts.Add(new CareTeamAlert(reading.Id, now));
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        cache.Remove(LatestReadingCache.Key(patientId));
        logger.LogInformation("Reading {ReadingId} saved; care team alert queued: {AlertQueued}", reading.Id, reading.NeedsAlert);
        return new AddReadingResult(reading);
    }

    /// <summary>Attaches a photo to one of the patient's own readings.</summary>
    /// <param name="readingId">The reading.</param>
    /// <param name="file">A file that passed <see cref="PhotoStore.IsAcceptable"/>.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns><c>false</c> when the reading doesn't exist or isn't the patient's.</returns>
    public async Task<bool> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var patientId = patient.Id;
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patientId, ct);
        if (reading is null)
        {
            return false;
        }

        var previous = reading.PhotoPath;
        var path = await photos.SaveAsync(readingId, file, ct);
        reading.AttachPhoto(path);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            photos.Delete(path);   // no row points at it: don't leave a patient's photo behind
            throw;
        }

        if (previous is not null && previous != path)
        {
            photos.Delete(previous);
        }

        return true;
    }
}
