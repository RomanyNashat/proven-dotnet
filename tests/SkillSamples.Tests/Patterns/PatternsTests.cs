using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Patterns;

public sealed class SentLog
{
    public List<string> Lines { get; } = [];
}

public sealed class SmsSender(SentLog log) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task SendAsync(string to, string text, CancellationToken ct)
    {
        log.Lines.Add($"sms:{to}:{text}");
        return Task.CompletedTask;
    }
}

public sealed class EmailSender(SentLog log) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(string to, string text, CancellationToken ct)
    {
        log.Lines.Add($"email:{to}:{text}");
        return Task.CompletedTask;
    }
}

public sealed class CountingDirectory : IClinicDirectory
{
    public int Calls { get; private set; }

    public Task<ClinicInfo?> FindAsync(int clinicId, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(clinicId == 1 ? new ClinicInfo(1, "North Clinic", "عيادة الشمال") : null);
    }
}

public sealed class PatternsTests
{
    [Fact]
    public async Task Router_PicksTheSenderForTheChannel()
    {
        var services = new ServiceCollection().AddSingleton<SentLog>()
            .AddSingleton<INotificationSender, SmsSender>()
            .AddSingleton<INotificationSender, EmailSender>()
            .AddSingleton<NotificationRouter>();
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<NotificationRouter>().SendAsync(NotificationChannel.Email, "a@b.c", "hi", default);

        Assert.Equal(new[] { "email:a@b.c:hi" }, sp.GetRequiredService<SentLog>().Lines);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sp.GetRequiredService<NotificationRouter>().SendAsync(NotificationChannel.Push, "x", "y", default));
    }

    [Fact]
    public void Router_TwoSendersForOneChannel_FailsAtConstruction()
    {
        var log = new SentLog();
        Assert.Throws<ArgumentException>(() => new NotificationRouter([new SmsSender(log), new SmsSender(log)]));
    }

    [Fact]
    public async Task KeyedService_InjectsTheSenderForTheKey()
    {
        var services = new ServiceCollection().AddSingleton<SentLog>()
            .AddKeyedSingleton<INotificationSender, SmsSender>(NotificationChannel.Sms)
            .AddKeyedSingleton<INotificationSender, EmailSender>(NotificationChannel.Email)
            .AddSingleton<OtpService>();
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<OtpService>().SendCodeAsync("0500000000", "1234", default);

        Assert.Equal(new[] { "sms:0500000000:Your code is 1234" }, sp.GetRequiredService<SentLog>().Lines);
    }

    [Fact]
    public async Task Decorator_CachesHitsAndMisses()
    {
        using var sp = new ServiceCollection().AddClinicDirectory<CountingDirectory>().BuildServiceProvider();
        using var scope = sp.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IClinicDirectory>();

        Assert.IsType<CachedClinicDirectory>(directory);
        Assert.Equal("North Clinic", (await directory.FindAsync(1, default))!.NameEn);
        Assert.Equal("North Clinic", (await directory.FindAsync(1, default))!.NameEn);
        Assert.Null(await directory.FindAsync(99, default));
        Assert.Null(await directory.FindAsync(99, default));
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<CountingDirectory>().Calls);
    }

    [Theory]
    [InlineData(AppointmentTrigger.CheckIn, AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentTrigger.Cancel, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentTrigger.MissWindow, AppointmentStatus.NoShow)]
    public void Appointment_FromBooked_MovesToTheNextState(AppointmentTrigger trigger, AppointmentStatus expected)
    {
        var appointment = new Appointment();

        Assert.True(appointment.TryApply(trigger));
        Assert.Equal(expected, appointment.Status);
    }

    [Fact]
    public void Appointment_TransitionNotInTheTable_IsRefusedAndStateKept()
    {
        var appointment = new Appointment();
        appointment.TryApply(AppointmentTrigger.Cancel);

        Assert.False(appointment.TryApply(AppointmentTrigger.CheckIn));
        Assert.False(appointment.TryApply(AppointmentTrigger.Complete));
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_TheNoShowJobRunsLateAfterTheVisitIsDone_ItCantMarkThePatientAsMissing()
    {
        // Given: the patient checks in and the doctor completes the visit
        var appointment = new Appointment();
        Assert.True(appointment.TryApply(AppointmentTrigger.CheckIn));
        Assert.True(appointment.TryApply(AppointmentTrigger.Complete));

        // When: the no-show job, running late, marks missed appointments; then someone tries to cancel it
        var markedMissing = appointment.TryApply(AppointmentTrigger.MissWindow);
        var cancelled = appointment.TryApply(AppointmentTrigger.Cancel);

        // Then: both are refused because the table doesn't list them, and the visit stays completed
        Assert.False(markedMissing);
        Assert.False(cancelled);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheRouterSendsArabicTextUnchanged()
    {
        ProductionConditions.Require();
        var services = new ServiceCollection().AddSingleton<SentLog>()
            .AddSingleton<INotificationSender, SmsSender>()
            .AddSingleton<INotificationSender, EmailSender>()
            .AddSingleton<NotificationRouter>();
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<NotificationRouter>().SendAsync(NotificationChannel.Sms, "0500000000", "موعدك غداً الساعة ٩", default);

        Assert.Equal(new[] { "sms:0500000000:موعدك غداً الساعة ٩" }, sp.GetRequiredService<SentLog>().Lines);
    }

    [Fact]
    public void Specs_ComposeIntoOneLambdaWithNoInvocation()
    {
        var from = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        AppointmentRow[] rows =
        [
            new(1, 7, from.AddHours(1), AppointmentStatus.Booked),
            new(2, 7, from.AddHours(2), AppointmentStatus.Cancelled),
            new(3, 7, from.AddHours(-1), AppointmentStatus.Booked),
            new(4, 8, from.AddHours(1), AppointmentStatus.Booked),
        ];

        var rule = AppointmentSpecs.ForClinic(7).And(AppointmentSpecs.StartingFrom(from)).And(AppointmentSpecs.NotCancelled);

        Assert.Equal(new[] { 1 }, rows.AsQueryable().Where(rule).Select(r => r.Id).ToArray());
        Assert.Single(rule.Parameters);
        Assert.False(new InvocationFinder().Contains(rule));
        Assert.Equal(new[] { 2, 3, 4 }, rows.AsQueryable().Where(rule.Not()).Select(r => r.Id).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.AsQueryable()
            .Where(AppointmentSpecs.ForClinic(7).Or(AppointmentSpecs.ForClinic(8))).Select(r => r.Id).ToArray());
    }

    private sealed class InvocationFinder : ExpressionVisitor
    {
        private bool _found;

        public bool Contains(Expression expression)
        {
            Visit(expression);
            return _found;
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            _found = true;
            return base.VisitInvocation(node);
        }
    }
}
