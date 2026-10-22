using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    public async Task<Reading> AddAsync(NewReading request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patient.Id, request.NationalId, decimal.Parse(request.Value), request.Unit,
            request.CollectedAt, request.Notes, now);
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);

        if (reading.NeedsAlert)
        {
            db.Alerts.Add(new CareTeamAlert(reading.Id, now));
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Reading received {@Reading}", reading);
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
