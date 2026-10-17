using ClinicReminders.Application;
using ClinicReminders.Domain;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public class Tests
{
    [Fact]
    public void MessageIsArabicGregorianRiyadhTime()
    {
        // 2026-10-17 21:30 UTC = 2026-10-18 00:30 Riyadh (Sunday)
        var text = ReminderService.MessageText(new DateTimeOffset(2026, 10, 17, 21, 30, 0, TimeSpan.Zero));
        Assert.Equal("موعدك يوم الأحد 18 أكتوبر 2026 الساعة 00:30", text);
    }

    [Fact]
    public void CursorRoundTrips()
    {
        Assert.True(ReminderQueries.TryParseCursor("639000000000000000.5", out var v));
        Assert.Equal(5, v.Id);
        Assert.False(ReminderQueries.TryParseCursor("2026-01-01T00:00:00+03:00", out _));
    }

    [Fact]
    public async Task DueWindowIsTodayAndTomorrowRiyadh()
    {
        var now = new DateTimeOffset(2026, 10, 17, 6, 0, 0, TimeSpan.Zero); // 09:00 Riyadh
        var time = new FakeTimeProvider(now);
        await using var db = new RemindersDbContext(new DbContextOptionsBuilder<RemindersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        DateTimeOffset At(int days, int h) => new DateTimeOffset(2026, 10, 17 + days, h, 0, 0, TimeSpan.FromHours(3));
        db.Reminders.AddRange(
            new Reminder(1, "+966500000000", At(0, 15), now),   // due
            new Reminder(1, "+966500000000", At(1, 10), now),   // due (tomorrow 10:00, >24h away)
            new Reminder(1, "+966500000000", At(0, 8), now),    // already past
            new Reminder(1, "+966500000000", At(2, 10), now));  // day after tomorrow
        await db.SaveChangesAsync();
        var due = await new DueReminders(db, time).TakeAsync(default);
        Assert.Equal(2, due.Count);
    }
}
