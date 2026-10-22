using HomeReadings.Application;
using HomeReadings.Domain;

namespace HomeReadings.Tests.Application;

public sealed class NewReadingValidatorTests
{
    private static NewReading Request(
        string? nationalId = "1012345678", string? value = "6.2", string? unit = "mmol/L", DateTime? collectedAt = null, string? notes = null) =>
        new(nationalId, value, unit, collectedAt ?? new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc), notes);

    [Fact]
    public void Validate_GoodRequest_GivesTheReading()
    {
        var errors = NewReadingValidator.Validate(Request(unit: "mg/dl", value: "١٨٠", nationalId: "١٠١٢٣٤٥٦٧٨"), out var reading);

        errors.Should().BeEmpty();
        reading.Should().Be(new ValidReading("1012345678", new MeterValue(180m, null), "mg/dL",
            new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc), null));
    }

    [Fact]
    public void Validate_CollectedAtWithoutOffset_IsRiyadhTime()
    {
        NewReadingValidator.Validate(Request(collectedAt: new DateTime(2026, 10, 22, 8, 30, 0, DateTimeKind.Unspecified)), out var reading);

        reading!.CollectedAtUtc.Should().Be(new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc));
        reading.CollectedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Validate_CollectedAtLocal_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc).ToLocalTime();

        NewReadingValidator.Validate(Request(collectedAt: local), out var reading);

        reading!.CollectedAtUtc.Should().Be(new DateTime(2026, 10, 22, 5, 30, 0, DateTimeKind.Utc));
        reading.CollectedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Validate_EverythingWrong_NamesEveryField()
    {
        var errors = NewReadingValidator.Validate(new NewReading("12", "abc", "kg", default, new string('x', 501)), out var reading);

        reading.Should().BeNull();
        errors.Keys.Should().BeEquivalentTo("nationalId", "value", "unit", "collectedAt", "notes");
    }

    [Fact]
    public void Validate_BlankNotes_AreDropped()
    {
        NewReadingValidator.Validate(Request(notes: "   "), out var reading);

        reading!.Notes.Should().BeNull();
    }
}
