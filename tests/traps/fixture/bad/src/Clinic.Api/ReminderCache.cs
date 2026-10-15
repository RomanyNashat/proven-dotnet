namespace Clinic.Api;

public sealed class ReminderCache(ClinicDbContext db)
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
    private static readonly CultureInfo Arabic = new CultureInfo("ar-SA");

    public string Format(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Riyadh).ToString("D", Arabic);
}
