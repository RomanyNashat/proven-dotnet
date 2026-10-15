using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Tdd;

// The version a logging line usually starts as: the whole command, "for context".
public sealed class LogTheCommandHandler(ILogger<LogTheCommandHandler> logger)
{
    public void Handle(CancelBooking command) => logger.LogInformation("Cancelling {@Command}", command);
}

public sealed class CancelBookingLogTests
{
    private static readonly CancelBooking Command = new(BookingId: 4821, PatientNationalId: "1089234567", Reason: "Rana is travelling");

    private static bool Mentions(FakeLogRecord record, string value) =>
        record.Message.Contains(value, StringComparison.Ordinal)
        || (record.StructuredState ?? []).Any(field => field.Value?.Contains(value, StringComparison.Ordinal) == true);

    [Fact]
    public void Handle_LogsTheBookingIdAsAField()
    {
        var logger = new FakeLogger<CancelBookingHandler>();

        new CancelBookingHandler(logger).Handle(Command);

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("4821", record.GetStructuredStateValue("BookingId"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_SupportLooksUpACancelledBooking_TheLineHasTheBookingButNothingAboutThePatient()
    {
        // Given: a cancellation carrying the patient's national ID and a free-text reason with her name
        var careful = new FakeLogger<CancelBookingHandler>();
        var naive = new FakeLogger<LogTheCommandHandler>();

        // When: the handler logs it, and so does the version that logs the whole command
        new CancelBookingHandler(careful).Handle(Command);
        new LogTheCommandHandler(naive).Handle(Command);

        // Then: support can find the booking by its id; the careful line carries nothing about the patient,
        // and the test catches the naive one, before a log store ever does
        var line = Assert.Single(careful.Collector.GetSnapshot());
        Assert.Equal("4821", line.GetStructuredStateValue("BookingId"));
        Assert.False(Mentions(line, "1089234567"));
        Assert.False(Mentions(line, "Rana"));
        Assert.True(Mentions(Assert.Single(naive.Collector.GetSnapshot()), "1089234567"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcuNoTzdata_TheLineIsTheSame()
    {
        ProductionConditions.Require();
        var logger = new FakeLogger<CancelBookingHandler>();

        new CancelBookingHandler(logger).Handle(Command);

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal("Booking 4821 cancelled", record.Message);
        Assert.False(Mentions(record, "1089234567"));
    }
}
