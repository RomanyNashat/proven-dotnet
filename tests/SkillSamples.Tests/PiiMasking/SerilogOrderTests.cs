using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.PiiMasking;

/// <summary>UseSerilog() and EnableRedaction() in a real WebApplication: both orders are wrong, differently.</summary>
public sealed class SerilogOrderTests
{
    private const string NationalId = "1089234567";

    private static (List<string> Serilog, string Factory) Log(bool redactionFirst)
    {
        var sink = new CollectingSink();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddRedaction(r => r.SetRedactor<StarRedactor>(ClinicData.PatientIdentifier));
        if (redactionFirst)
        {
            builder.Logging.EnableRedaction();
        }

        builder.Host.UseSerilog((_, config) => config.WriteTo.Sink(sink));
        if (!redactionFirst)
        {
            builder.Logging.EnableRedaction();
        }

        using var app = builder.Build();
        BookingLog.BookingRefused(app.Services.GetRequiredService<ILogger<SerilogOrderTests>>(), 42, NationalId, "no slots");
        return (sink.Rendered, app.Services.GetRequiredService<ILoggerFactory>().GetType().Name);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_UseSerilogThenEnableRedaction_NothingReachesSerilog()
    {
        // Given: a service that calls UseSerilog(), then EnableRedaction() a line later
        // When: it logs a refused booking with a classified national ID
        var (serilog, factory) = Log(redactionFirst: false);

        // Then: EnableRedaction put its own logger factory back over Serilog's, and Serilog isn't a provider
        // under it. The line reaches no sink: the service's logs stop arriving, and nothing says so.
        Assert.True(serilog.Count == 0, $"factory {factory}, Serilog got: {string.Join(" / ", serilog)}");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_EnableRedactionThenUseSerilog_TheFieldVanishes()
    {
        // Given: the other order, EnableRedaction() first and UseSerilog() after it
        var (serilog, factory) = Log(redactionFirst: true);

        // Then: Serilog's factory replaced the redacting one. The line arrives, but the redactor never ran:
        // the classified field is gone, not masked, and no one can search by it.
        var line = Assert.Single(serilog);
        Assert.DoesNotContain(NationalId, line);
        Assert.False(line.Contains("****", StringComparison.Ordinal), $"factory {factory}: {line}");
    }
}
