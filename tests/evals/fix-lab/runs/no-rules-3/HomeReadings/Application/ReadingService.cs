using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HomeReadings.Api;
using HomeReadings.Domain;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Application;

public sealed record NewReading(string NationalId, string Value, string Unit, DateTime CollectedAt, string? Notes);

public sealed partial class ReadingService(ReadingsDbContext db, ICurrentPatient patient, TimeProvider time, PhotoStore photos, ILogger<ReadingService> logger)
{
    private const decimal MaxValue = 9999.99m;
    private const int MaxNotesLength = 2000;
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex NationalIdPattern();

    public async Task<Reading> AddAsync(NewReading request, CancellationToken ct)
    {
        if (request is null || request.NationalId is null || !NationalIdPattern().IsMatch(request.NationalId))
        {
            throw new RequestRejectedException("Invalid national id");
        }

        var value = ParseValue(request.Value);
        var unit = CanonicalUnit(request.Unit);
        if (request.Notes is { Length: > MaxNotesLength })
        {
            throw new RequestRejectedException("Notes are too long");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var reading = new Reading(patient.Id, request.NationalId, value, unit, ToUtc(request.CollectedAt), request.Notes, now);

        // The reading and its alert are saved together, so a high reading can't be stored without its alert.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Readings.Add(reading);
        await db.SaveChangesAsync(ct);

        if (reading.NeedsAlert)
        {
            db.Alerts.Add(new CareTeamAlert(reading.Id, now));
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        logger.LogInformation("Reading {ReadingId} received for patient {PatientId}", reading.Id, reading.PatientId);
        return reading;
    }

    public async Task<bool> AttachPhotoAsync(int readingId, IFormFile file, CancellationToken ct)
    {
        var reading = await db.Readings.FirstOrDefaultAsync(r => r.Id == readingId && r.PatientId == patient.Id, ct);
        if (reading is null)
        {
            return false;
        }

        var previous = reading.PhotoPath;
        var saved = await photos.SaveAsync(readingId, file, ct);
        try
        {
            reading.AttachPhoto(saved);
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

        return true;
    }

    /// <summary>The app sends the value as the meter shows it: may use Arabic-Indic digits and a comma or Arabic decimal separator.</summary>
    internal static decimal ParseValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RequestRejectedException("Invalid reading value");
        }

        var normalized = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            normalized.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                ',' or '٫' or '٬' => '.',
                _ => c,
            });
        }

        if (!decimal.TryParse(normalized.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value <= 0 || value > MaxValue)
        {
            throw new RequestRejectedException("Invalid reading value");
        }

        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    internal static string CanonicalUnit(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "mmol/l" => Reading.Mmol,
        "mg/dl" => Reading.Mgdl,
        _ => throw new RequestRejectedException("Unsupported unit"),
    };

    /// <summary>timestamptz needs UTC. A time with no offset is taken as Riyadh time, where the patients are.</summary>
    internal static DateTime ToUtc(DateTime collectedAt) => collectedAt.Kind switch
    {
        DateTimeKind.Utc => collectedAt,
        DateTimeKind.Local => collectedAt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(collectedAt - Riyadh, DateTimeKind.Utc),
    };
}
