using HomeReadings.Domain;
using HomeReadings.Infrastructure;

namespace HomeReadings.Application;

/// <summary>A <see cref="NewReading"/> that passed validation, in the forms the service stores.</summary>
public sealed record ValidReading(string NationalId, decimal Value, string Unit, DateTime CollectedAtUtc, string? Notes);

/// <summary>Checks a reading from the app before anything is stored or sent to a nurse.</summary>
public static class NewReadingValidator
{
    /// <summary>Meter and phone clocks drift; a reading a little ahead of the server is still accepted.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);

    private const decimal MaxMmolPerLitre = 50m;
    private const decimal MaxMgPerDl = 900m;
    private static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ValidReading? Validate(NewReading request, DateTime nowUtc, out Dictionary<string, string[]> errors)
    {
        errors = [];

        var nationalId = MeterText.NormalizeDigits(request.NationalId?.Trim() ?? "");
        if (!IsNationalId(nationalId))
        {
            errors["nationalId"] = ["Must be a 10-digit Saudi national ID or Iqama number."];
        }

        var unit = Reading.NormalizeUnit(request.Unit);
        if (unit is null)
        {
            errors["unit"] = [$"Must be {Reading.MmolPerLitre} or {Reading.MgPerDecilitre}."];
        }

        if (!MeterText.TryParseValue(request.Value, out var value) || value <= 0)
        {
            errors["value"] = ["Must be the number the meter shows, for example 7.5."];
        }
        else if (unit is not null && value > (unit == Reading.MgPerDecilitre ? MaxMgPerDl : MaxMmolPerLitre))
        {
            errors["value"] = [$"Is too high for a glucose reading in {unit}."];
        }

        var collectedAt = RiyadhTime.ToUtc(request.CollectedAt);
        if (collectedAt < Earliest)
        {
            errors["collectedAt"] = ["Is required."];
        }
        else if (collectedAt > nowUtc + FutureTolerance)
        {
            errors["collectedAt"] = ["Is in the future."];
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > ReadingsDbContext.NotesMaxLength)
        {
            errors["notes"] = [$"Must be at most {ReadingsDbContext.NotesMaxLength} characters."];
        }

        return errors.Count > 0
            ? null
            : new ValidReading(nationalId, Math.Round(value, 2, MidpointRounding.AwayFromZero), unit!, collectedAt, notes);
    }

    // Saudi national IDs start with 1, Iqama (resident) numbers with 2; both are 10 digits.
    private static bool IsNationalId(string id) =>
        id.Length == 10 && id[0] is ('1' or '2') &&id.All(char.IsAsciiDigit);
}
