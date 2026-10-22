using System.Globalization;
using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace HomeReadings.Application;

/// <summary>As the app sends it: the value is the meter's text, always with '.', and the time carries the phone's offset.</summary>
public sealed record NewReading(string NationalId, string Value, string Unit, DateTimeOffset CollectedAt, string? Notes);

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos,
    IMemoryCache cache, ILogger<ReadingService> logger)
{
    public static string? Problem(NewReading request) => request switch
    {
        { NationalId: not { Length: 10 } } => "nationalId",
        _ when !request.NationalId.All(char.IsAsciiDigit) => "nationalId",
        { Unit: null or { Length: 0 or > 16 } } => "unit",
        { Notes.Length: > 500 } => "notes",
        _ when !decimal.TryParse(request.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) => "value",
        _ => null,
    };

    public async Task<Reading> AddAsync(NewReading request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var value = decimal.Parse(request.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
        var reading = new Reading(patient.Id, request.NationalId, value, request.Unit,
            request.CollectedAt.UtcDateTime, request.Notes, now);
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);

        if (reading.NeedsAlert)
        {
            db.Alerts.Add(new CareTeamAlert(reading.Id, now));
            await db.SaveChangesAsync(ct);
        }

        cache.Remove(ReadingQueries.LatestKey(patient.Id));
        logger.LogInformation("Reading {ReadingId} received", reading.Id);
        return reading;
    }

    public async Task<bool> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.FindAsync([readingId], ct);
        if (reading is null || reading.PatientId != patient.Id)
        {
            return false;
        }

        reading.AttachPhoto(await photos.SaveAsync(readingId, file, ct));
        await db.SaveChangesAsync(ct);
        return true;
    }
}
