using System.Net;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using ClinicReminders.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ClinicReminders.Tests.Application;

public sealed class ReminderDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);
    private readonly Mock<IDueReminders> _due = new();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task SendDueAsync_ProviderAccepts_ClaimsSendsAndRecordsSent()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)));
        Claimable(1);
        var sms = StubSmsHandler.Answering(HttpStatusCode.OK);

        var summary = await Dispatcher(sms).SendDueAsync(CancellationToken.None);

        summary.Sent.Should().Be(1);
        sms.Requests.Should().ContainSingle();
        _due.Verify(d => d.MarkSentAsync(1, Now, It.IsAny<CancellationToken>()), Times.Once);
        _due.Verify(d => d.ReleaseAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendDueAsync_AnotherRunClaimedItFirst_DoesNotSend()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)));
        _due.Setup(d => d.TryClaimAsync(1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sms = StubSmsHandler.Answering(HttpStatusCode.OK);

        var summary = await Dispatcher(sms).SendDueAsync(CancellationToken.None);

        summary.Skipped.Should().Be(1);
        sms.Requests.Should().BeEmpty();
        _due.Verify(d => d.MarkSentAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendDueAsync_ProviderRefuses_ReleasesTheClaimForTheNextRun()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)));
        Claimable(1);

        var summary = await Dispatcher(StubSmsHandler.Answering(HttpStatusCode.ServiceUnavailable)).SendDueAsync(CancellationToken.None);

        summary.NotSent.Should().Be(1);
        _due.Verify(d => d.ReleaseAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _due.Verify(d => d.MarkSentAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendDueAsync_ProviderTimesOut_KeepsTheClaimAndCarriesOn()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)), new DueReminder(2, "0507654321", Now.AddHours(6)));
        Claimable(1, 2);
        var calls = 0;
        var sms = new StubSmsHandler((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var summary = await Dispatcher(sms).SendDueAsync(CancellationToken.None);

        summary.Should().Be(new DispatchSummary(Sent: 1, NotSent: 0, Unknown: 1, Failed: 0, Skipped: 0));
        _due.Verify(d => d.ReleaseAsync(1, It.IsAny<CancellationToken>()), Times.Never);   // it may have gone out
        _due.Verify(d => d.MarkSentAsync(2, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendDueAsync_OneRowThrows_TheOthersStillGoOut()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)), new DueReminder(2, "0507654321", Now.AddHours(6)));
        _due.Setup(d => d.TryClaimAsync(1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database hiccup"));
        Claimable(2);

        var summary = await Dispatcher(StubSmsHandler.Answering(HttpStatusCode.OK)).SendDueAsync(CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.Sent.Should().Be(1);
    }

    [Fact]
    public async Task SendDueAsync_ShutdownDuringSend_StopsWithoutReleasing()
    {
        Due(new DueReminder(1, "0501234567", Now.AddHours(5)), new DueReminder(2, "0507654321", Now.AddHours(6)));
        Claimable(1, 2);
        using var shutdown = new CancellationTokenSource();
        var sms = new StubSmsHandler(async (_, ct) =>
        {
            await shutdown.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var act = () => Dispatcher(sms).SendDueAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sms.Requests.Should().ContainSingle();
        _due.Verify(d => d.ReleaseAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendDueAsync_LooksOnlyAtTheNext24HoursAndPagesById()
    {
        _due.SetupSequence(d => d.FindAsync(It.IsAny<DueWindow>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. Enumerable.Range(1, ReminderDispatcher.BatchSize).Select(i => new DueReminder(i, "0501234567", Now.AddHours(1)))])
            .ReturnsAsync([]);
        _due.Setup(d => d.TryClaimAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Dispatcher(StubSmsHandler.Answering(HttpStatusCode.OK)).SendDueAsync(CancellationToken.None);

        var window = new DueWindow(Now, Now.AddDays(1));
        _due.Verify(d => d.FindAsync(window, 0, ReminderDispatcher.BatchSize, It.IsAny<CancellationToken>()), Times.Once);
        _due.Verify(d => d.FindAsync(window, ReminderDispatcher.BatchSize, ReminderDispatcher.BatchSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void Due(params DueReminder[] reminders) =>
        _due.SetupSequence(d => d.FindAsync(It.IsAny<DueWindow>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminders)
            .ReturnsAsync([]);

    private void Claimable(params int[] ids)
    {
        foreach (var id in ids)
        {
            _due.Setup(d => d.TryClaimAsync(id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }
    }

    private ReminderDispatcher Dispatcher(HttpMessageHandler sms) =>
        new(_due.Object, new SmsClient(new HttpClient(sms) { BaseAddress = new Uri("https://sms.example.net/api/v2/") }),
            _time, NullLogger<ReminderDispatcher>.Instance);
}
