using ClinicReminders.Infrastructure;

namespace ClinicReminders.Tests.Infrastructure;

public sealed class ReminderCursorTests
{
    [Fact]
    public void TryParse_EncodedCursor_RoundTrips()
    {
        var cursor = new ReminderCursor(new DateTimeOffset(2026, 10, 17, 6, 0, 0, 123, TimeSpan.Zero), 42);

        ReminderCursor.TryParse(cursor.Encode(), out var parsed).Should().BeTrue();

        parsed.Should().Be(cursor);
    }

    [Fact]
    public void TryParse_NoCursor_IsTheFirstPage()
    {
        ReminderCursor.TryParse(null, out var none).Should().BeTrue();
        none.Should().BeNull();
        ReminderCursor.TryParse("", out var empty).Should().BeTrue();
        empty.Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("2026-10-17T06:00:00Z")]
    [InlineData("%%%")]
    [InlineData("MTIz")]   // base64url of "123": no id part
    public void TryParse_Garbage_Fails(string value)
    {
        ReminderCursor.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Encode_AnyCursor_IsUrlSafe()
    {
        var encoded = new ReminderCursor(DateTimeOffset.MaxValue.AddDays(-1), int.MaxValue).Encode();

        encoded.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }
}
