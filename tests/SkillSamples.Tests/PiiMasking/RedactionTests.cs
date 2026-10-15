using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.PiiMasking;

public sealed class RedactionTests
{
    private const string NationalId = "1089234567";

    private sealed class Collect : ILogEventSink
    {
        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent) => Lines.Add(
            logEvent.RenderMessage(CultureInfo.InvariantCulture) + " | " +
            string.Join(", ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}")));
    }

    // The generator may name a tag after the parameter or the placeholder; the test doesn't depend on which.
    private static string? Field(FakeLogRecord record, string name) =>
        record.StructuredState?.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static (ServiceProvider Services, FakeLogCollector Logs) WithFakeLogger()
    {
        var services = new ServiceCollection().AddClassifiedLogging(logging => logging.AddFakeLogging()).BuildServiceProvider();
        return (services, services.GetFakeLogCollector());
    }

    [Fact]
    public void ClassifiedParameter_IsRedacted_InTheMessageAndTheField()
    {
        var (services, logs) = WithFakeLogger();
        using var _ = services;

        BookingLog.BookingRefused(services.GetRequiredService<ILogger<RedactionTests>>(), 42, NationalId, "no slots");

        var line = logs.LatestRecord;
        Assert.Equal("Booking 42 refused for ****: no slots", line.Message);
        Assert.Equal("****", Field(line, "NationalId"));
        Assert.Equal("42", Field(line, "BookingId"));
    }

    [Fact]
    public void PlainTemplate_IsNotRedacted_ClassificationOnlyCoversWhatIsClassified()
    {
        var (services, logs) = WithFakeLogger();
        using var _ = services;

        // The same value through an ordinary call: nothing marks it, so the redactor never sees it. This is
        // why the Serilog pattern operators stay as the second layer.
#pragma warning disable CA1848, CA2254 // the point of the test is the ordinary call
        services.GetRequiredService<ILogger<RedactionTests>>().LogWarning("Booking {BookingId} refused for {NationalId}", 42, NationalId);
#pragma warning restore CA1848, CA2254

        Assert.Contains(NationalId, logs.LatestRecord.Message);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_SerilogTakesOverTheLoggerFactory_TheRedactorStopsRunning()
    {
        // Given: a service whose national IDs are classified and redacted, logging through Serilog as a
        // provider, the way RedactionSetup wires it
        var asProvider = new Collect();
        using (var services = new ServiceCollection()
                   .AddClassifiedLogging(logging => logging.AddSerilog(new LoggerConfiguration().WriteTo.Sink(asProvider).CreateLogger(), dispose: true))
                   .BuildServiceProvider())
        {
            BookingLog.BookingRefused(services.GetRequiredService<ILogger<RedactionTests>>(), 42, NationalId, "no slots");
        }

        // When: someone moves the service to UseSerilog(), which registers Serilog as the logger factory
        var asFactory = new Collect();
        using (var services = new ServiceCollection()
                   .AddClassifiedLogging(_ => { })
                   .AddSingleton<ILoggerFactory>(new SerilogLoggerFactory(new LoggerConfiguration().WriteTo.Sink(asFactory).CreateLogger(), dispose: true))
                   .BuildServiceProvider())
        {
            BookingLog.BookingRefused(services.GetRequiredService<ILogger<RedactionTests>>(), 42, NationalId, "no slots");
        }

        // Then: as a provider, Elastic gets the line with the ID redacted. With Serilog as the factory the
        // redactor never runs: the classified field doesn't leak, it disappears, and nothing says so
        var provided = Assert.Single(asProvider.Lines);
        Assert.Contains("****", provided);
        Assert.DoesNotContain(NationalId, provided);
        var replaced = Assert.Single(asFactory.Lines);
        Assert.DoesNotContain(NationalId, replaced);
        Assert.DoesNotContain("****", replaced);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcuNoTzdata_ArabicIndicDigits_AreRedactedLikeAnyOther()
    {
        ProductionConditions.Require();
        var (services, logs) = WithFakeLogger();
        using var _ = services;

        BookingLog.BookingRefused(services.GetRequiredService<ILogger<RedactionTests>>(), 42, "١٠٨٩٢٣٤٥٦٧", "لا توجد مواعيد");

        Assert.Equal("Booking 42 refused for ****: لا توجد مواعيد", logs.LatestRecord.Message);
    }
}
