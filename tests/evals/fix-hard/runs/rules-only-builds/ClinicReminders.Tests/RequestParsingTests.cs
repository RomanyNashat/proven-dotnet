using System.Text.Json;
using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Tests;

public sealed class RequestParsingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new ExplicitOffsetConverter() } };
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("\"2026-11-02T10:30:00+03:00\"", "2026-11-02T07:30:00Z")]
    [InlineData("\"2026-11-02T07:30:00Z\"", "2026-11-02T07:30:00Z")]
    public void ExplicitOffsetConverter_WithOffset_ReadsTheInstant(string json, string expectedUtc)
    {
        var value = JsonSerializer.Deserialize<DateTimeOffset>(json, Json);

        value.Should().Be(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ExplicitOffsetConverter_WithoutOffset_IsRefused()
    {
        var read = () => JsonSerializer.Deserialize<DateTimeOffset>("\"2026-11-02T10:30:00\"", Json);

        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void Validate_GoodRequest_HasNoErrors() =>
        new ScheduleReminder(7, "+966500000001", Now.AddDays(2)).Validate(Now).Should().BeEmpty();

    [Theory]
    [InlineData(0, "+966500000001", 2, "PatientId")]
    [InlineData(7, "not a number", 2, "Mobile")]
    [InlineData(7, "+966500000001", -1, "AppointmentAt")]
    [InlineData(7, "+966500000001", 400, "AppointmentAt")]
    public void Validate_BadField_NamesIt(int patientId, string mobile, int daysAhead, string field) =>
        new ScheduleReminder(patientId, mobile, Now.AddDays(daysAhead)).Validate(Now).Should().ContainKey(field);

    [Fact]
    public void Validate_MissingMobile_NamesIt() =>
        new ScheduleReminder(7, null!, Now.AddDays(2)).Validate(Now).Should().ContainKey("Mobile");

    [Fact]
    public void Cursor_RoundTrips()
    {
        var cursor = new ReminderCursor(new DateTimeOffset(2026, 10, 17, 9, 0, 0, 123, TimeSpan.Zero), 42);

        ReminderCursor.TryDecode(cursor.Encode(), out var decoded).Should().BeTrue();
        decoded.Should().Be(cursor);
    }

    [Theory]
    [InlineData("2026-10-17T09:00:00Z")]
    [InlineData("!!!")]
    [InlineData("MTIz")]
    public void Cursor_NotOneOfOurs_IsRefused(string text) =>
        ReminderCursor.TryDecode(text, out _).Should().BeFalse();
}
