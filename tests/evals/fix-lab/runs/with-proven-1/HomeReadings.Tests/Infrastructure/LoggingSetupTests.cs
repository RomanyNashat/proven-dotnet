using HomeReadings.Infrastructure;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HomeReadings.Tests.Infrastructure;

public sealed class LoggingSetupTests
{
    private sealed class Collector : ILogEventSink
    {
        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent) => Lines.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1012345678")]
    [InlineData("2098765432")]
    [InlineData("١٠١٢٣٤٥٦٧٨")]
    public void AddPiiMasking_NationalIdInALine_IsMaskedButForTheLastFour(string nationalId)
    {
        var collector = new Collector();
        using (var logger = new LoggerConfiguration().AddPiiMasking().WriteTo.Sink(collector).CreateLogger())
        {
            logger.Information("Care team refused alert for {NationalId}", nationalId);
        }

        collector.Lines.Should().ContainSingle().Which.Should().NotContain(nationalId).And.Contain(nationalId[^4..]);
    }
}
