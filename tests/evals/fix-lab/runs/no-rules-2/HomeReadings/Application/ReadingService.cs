using System.Globalization;
using System.Text;
using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed class InvalidReadingException(string message) : Exception(message);

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    private const int MaxNotesLength = 1000;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);

    public async Task<Reading> AddAsync(NewReading request, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var nationalId = NormalizeDigits(request.NationalId?.Trim() ?? "");
        if (nationalId.Length != 10 || !nationalId.All(char.IsAsciiDigit))
        {
            throw new InvalidReadingException("Invalid national id");
        }

        var unit = request.Unit?.Trim().ToLowerInvariant() switch
        {
            "mmol/l" => Reading.UnitMmolL,
            "mg/dl" => Reading.UnitMgDl,
            _ => throw new InvalidReadingException("Unknown unit"),
        };
        var value = ParseValue(request.Value);
        if (value <= 0 || value > (unit == Reading.UnitMgDl ? 2000m : 111m))
        {
            throw new InvalidReadingException("Value out of range");
        }

        var collectedAt = ToUtc(request.CollectedAt);
        if (collectedAt > now + FutureTolerance)
        {
            throw new InvalidReadingException("Collection time is in the future");
        }

        if (request.Notes is { Length: > MaxNotesLength })
        {
            throw new InvalidReadingException("Notes are too long");
        }

        var reading = new Reading(patient.Id, nationalId, value, unit, collectedAt, request.Notes, now);

        // The reading and its alert are saved together: a crash must not leave a high reading with no alert.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);

        if (reading.NeedsAlert)
        {
            db.Alerts.Add(new CareTeamAlert(reading.Id, now));
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        // No national id or notes in the logs: they identify the patient.
        logger.LogInformation("Reading {ReadingId} received for patient {PatientId}", reading.Id, reading.PatientId);
        return reading;
    }

    public async Task<bool> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.FindAsync([readingId], ct);
        if (reading is null || reading.PatientId != patient.Id)
        {
            return false;
        }

        var previous = reading.PhotoPath;
        reading.AttachPhoto(await photos.SaveAsync(readingId, file, ct));
        await db.SaveChangesAsync(ct);
        photos.Delete(previous);
        return true;
    }

    /// <summary>The meter's text as shown: may use Arabic-Indic digits and a comma or Arabic decimal separator.</summary>
    private static decimal ParseValue(string? text)
    {
        var normalized = NormalizeDigits(text?.Trim() ?? "").Replace('٫', '.').Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidReadingException("Value is not a number");
        }

        return Math.Round(value, 2);
    }

    private static string NormalizeDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            });
        }

        return sb.ToString();
    }

    /// <summary>timestamptz needs UTC. A time with no offset is the patient's wall clock in Riyadh.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value - ReadingQueries.RiyadhOffset, DateTimeKind.Utc),
    };
}
