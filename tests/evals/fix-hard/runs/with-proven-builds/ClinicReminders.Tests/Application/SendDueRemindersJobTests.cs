using System.Net;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using ClinicReminders.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Quartz;

namespace ClinicReminders.Tests.Application;

public sealed class SendDueRemindersJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);
    private readonly Mock<IDueReminders> _due = new();
    private readonly StubSmsHandler _sms = StubSmsHandler.Answering(HttpStatusCode.OK);

    [Fact]
    public async Task Execute_DueReminder_SendsItInItsOwnScope()
    {
        _due.SetupSequence(d => d.FindAsync(It.IsAny<DueWindow>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DueReminder(5, "0501234567", Now.AddHours(3))])
            .ReturnsAsync([]);
        _due.Setup(d => d.TryClaimAsync(5, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var services = Services();

        await services.GetRequiredService<SendDueRemindersJob>().Execute(Context());

        _sms.Requests.Should().ContainSingle();
        _due.Verify(d => d.MarkSentAsync(5, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DatabaseDown_FailsTheRunWithoutRefiring()
    {
        _due.Setup(d => d.FindAsync(It.IsAny<DueWindow>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));
        using var services = Services();

        var act = () => services.GetRequiredService<SendDueRemindersJob>().Execute(Context());

        (await act.Should().ThrowAsync<JobExecutionException>()).Which.RefireImmediately.Should().BeFalse();
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddScoped(_ => _due.Object);
        services.AddScoped<ReminderDispatcher>();
        services.AddTransient<SendDueRemindersJob>();
        services.AddHttpClient<SmsClient>(c => c.BaseAddress = new Uri("https://sms.example.net/api/v2/"))
            .ConfigurePrimaryHttpMessageHandler(() => _sms);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static IJobExecutionContext Context() =>
        Mock.Of<IJobExecutionContext>(c => c.CancellationToken == CancellationToken.None);
}
