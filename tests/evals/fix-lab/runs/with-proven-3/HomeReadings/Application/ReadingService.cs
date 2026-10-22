using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    public async Task<AddReadingResult> AddAsync(NewReading request, CancellationToken ct)
    {
        var errors = NewReadingValidator.Validate(request, out var valid);
        if (valid is null)
        {
            return new AddReadingResult(null, errors);
        }

        // The national ID goes on the care team's alert, so a patient can't file readings under someone
        // else's. Until it comes from the identity provider or a patient registry, it must at least match
        // the one on this patient's earlier readings.
        var knownNationalId = await db.Readings.AsNoTracking()
            .Where(r => r.PatientId == patient.Id)
            .OrderBy(r => r.Id)
            .Select(r => r.NationalId)
            .FirstOrDefaultAsync(ct);
        if (knownNationalId is not null && knownNationalId != valid.NationalId)
        {
            return new AddReadingResult(null, new Dictionary<string, string[]>
            {
                ["nationalId"] = ["Does not match this patient's national ID."],
            });
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patient.Id, valid.NationalId, valid.Meter, valid.Unit, valid.CollectedAtUtc, valid.Notes, now);
        db.Readings.Add(reading);
        if (reading.NeedsAlert)
        {
            // Same SaveChanges, so the same transaction: a high reading is never stored without its alert.
            db.Alerts.Add(new CareTeamAlert(reading, now));
        }

        await db.SaveChangesAsync(ct);

        // Ids only: the reading's value, notes and national ID are health data and stay out of the logs.
        logger.LogInformation("Reading {ReadingId} saved; care team alert raised: {AlertRaised}", reading.Id, reading.NeedsAlert);
        return new AddReadingResult(reading, errors);
    }

    public async Task<AttachPhotoResult> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.SingleOrDefaultAsync(r => r.Id == readingId && r.PatientId == patient.Id, ct);
        if (reading is null)
        {
            return AttachPhotoResult.NotFound;
        }

        var key = await photos.SaveAsync(readingId, file, ct);
        if (key is null)
        {
            return AttachPhotoResult.NotAnAcceptedImage;
        }

        reading.AttachPhoto(key);
        await db.SaveChangesAsync(ct);
        return AttachPhotoResult.Attached;
    }
}
