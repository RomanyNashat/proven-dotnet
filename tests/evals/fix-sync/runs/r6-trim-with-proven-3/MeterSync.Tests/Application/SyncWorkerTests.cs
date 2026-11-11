using MeterSync.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeterSync.Tests.Application;

public sealed class SyncWorkerTests
{
    [Fact]
    public async Task RunStepAsync_HttpTimeoutWhileRunning_IsLoggedNotThrown()
    {
        var act = () => SyncWorker.RunStepAsync(_ => throw new TaskCanceledException("HttpClient timeout"), "sync", NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunStepAsync_StepFails_IsLoggedNotThrown()
    {
        var act = () => SyncWorker.RunStepAsync(_ => throw new HttpRequestException("404"), "sync", NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunStepAsync_HostStopping_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SyncWorker.RunStepAsync(ct => Task.FromCanceled(ct), "sync", NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunStepAsync_StepSucceeds_RunsIt()
    {
        var ran = false;

        await SyncWorker.RunStepAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        }, "sync", NullLogger.Instance, CancellationToken.None);

        ran.Should().BeTrue();
    }
}
