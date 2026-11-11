using MeterSync.Application;
using Microsoft.Extensions.Logging;
using Moq;

namespace MeterSync.Tests.Application;

public sealed class SyncWorkerTests
{
    private readonly Mock<ILogger> _logger = new();

    [Fact]
    public async Task RunIteration_SyncTimesOut_StillSweepsAndDoesNotThrow()
    {
        var swept = false;

        var act = () => SyncWorker.RunIterationAsync(
            _ => throw new TaskCanceledException("HttpClient.Timeout of 100 seconds elapsing"),
            _ => { swept = true; return Task.CompletedTask; },
            _logger.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        swept.Should().BeTrue();
    }

    [Fact]
    public async Task RunIteration_SweepFails_DoesNotThrowAndLogsError()
    {
        var act = () => SyncWorker.RunIterationAsync(
            _ => Task.CompletedTask,
            _ => throw new HttpRequestException("gateway down"),
            _logger.Object,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunIteration_HostStopping_PropagatesCancellation()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var act = () => SyncWorker.RunIterationAsync(
            ct => Task.FromCanceled(ct),
            _ => Task.CompletedTask,
            _logger.Object,
            stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
