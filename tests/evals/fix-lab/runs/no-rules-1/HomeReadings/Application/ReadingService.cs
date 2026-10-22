using System.Globalization;
using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

/// <summary>A request the service can't accept; the messages are safe to show the patient.</summary>
public sealed class InvalidReadingException(IDictionary<string, string[]> errors) : Exception("Invalid reading")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    /// <summary>Saudi Arabia has no daylight saving, so a fixed offset is exact (and needs no tz database in the container).</summary>
    public static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    public async Task<Reading> AddAsync(NewReading request, CancellationToken ct)
    {
        var (value, unit, collectedAt) = Validate(request);

        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patient.Id, request.NationalId.Trim(), value, unit, collectedAt, request.Notes, now);
        db.Readings.Add(reading);

        // The reading and its alert are saved together so a crash can't lose the alert.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (reading.NeedsAlert)
        {
            db.Alerts.Add(new CareTeamAlert(reading.Id, now));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

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

    private static (decimal Value, string Unit, DateTime CollectedAt) Validate(NewReading request)
    {
        var errors = new Dictionary<string, string[]>();

        var nationalId = request.NationalId?.Trim();
        if (string.IsNullOrEmpty(nationalId) || nationalId.Length > 10)
        {
            errors["nationalId"] = ["nationalId is required and at most 10 characters."];
        }

        string unit = "";
        if (string.Equals(request.Unit?.Trim(), Reading.MmolPerL, StringComparison.OrdinalIgnoreCase))
        {
            unit = Reading.MmolPerL;
        }
        else if (string.Equals(request.Unit?.Trim(), Reading.MgPerDl, StringComparison.OrdinalIgnoreCase))
        {
            unit = Reading.MgPerDl;
        }
        else
        {
            errors["unit"] = [$"unit must be {Reading.MmolPerL} or {Reading.MgPerDl}."];
        }

        if (!TryParseMeterValue(request.Value, out var value) || value <= 0 || value > 9999.99m)
        {
            errors["value"] = ["value must be a positive number."];
        }

        if (request.Notes is { Length: > 2000 })
        {
            errors["notes"] = ["notes is too long."];
        }

        if (request.CollectedAt == default)
        {
            errors["collectedAt"] = ["collectedAt is required."];
        }

        if (errors.Count > 0)
        {
            throw new InvalidReadingException(errors);
        }

        return (Math.Round(value, 2), unit, ToUtc(request.CollectedAt));
    }

    /// <summary>A time with no offset is the patient's wall-clock time in Riyadh; timestamptz needs UTC.</summary>
    private static DateTime ToUtc(DateTime collectedAt) => collectedAt.Kind switch
    {
        DateTimeKind.Utc => collectedAt,
        DateTimeKind.Local => collectedAt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(collectedAt - RiyadhOffset, DateTimeKind.Utc),
    };

    /// <summary>
    /// Reads the text as the meter shows it: Western or Arabic-Indic digits, '.' or ',' or '٫' as the decimal mark.
    /// Never uses the request culture.
    /// </summary>
    public static bool TryParseMeterValue(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            switch (c)
            {
                case >= '٠' and <= '٩':
                    normalized.Append((char)('0' + (c - '٠')));
                    break;
                case >= '۰' and <= '۹':
                    normalized.Append((char)('0' + (c - '۰')));
                    break;
                case ',' or '٫':
                    normalized.Append('.');
                    break;
                case ' ' or ' ':
                    break;
                default:
                    normalized.Append(c);
                    break;
            }
        }

        return decimal.TryParse(normalized.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
