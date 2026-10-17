using ClinicReminders.Application;
using ClinicReminders.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicReminders.Tests.Api;

[Collection(nameof(HostCollection))]
public sealed class LoggingTests : IDisposable
{
    private readonly string _logFile = Path.Combine(Path.GetTempPath(), $"clinic-reminders-{Guid.CreateVersion7()}.log");

    [Fact]
    public async Task Log_MobileClassifiedOrNot_NeverReachesTheSink()
    {
        var factory = new ReminderApiFactory();
        factory.Settings["Serilog:WriteTo:0:Name"] = "File";
        factory.Settings["Serilog:WriteTo:0:Args:path"] = _logFile;
        factory.Settings["Serilog:WriteTo:0:Args:outputTemplate"] = "{Message:lj} {Properties:j}{NewLine}";
        _ = factory.Services;   // start the host
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        ReminderLog.Sent(logger, 41, "0501234567");
        logger.LogInformation("Free text with {Phone}", "+966507654321");
        await factory.DisposeAsync();   // flushes the sink

        var logged = await File.ReadAllTextAsync(_logFile);
        logged.Should().Contain("Reminder 41 sent");
        logged.Should().NotContain("0501234567").And.NotContain("507654321");
    }

    public void Dispose() => File.Delete(_logFile);
}
