namespace ClinicBooking.Infrastructure;

public sealed class ClinicSettingsCache
{
    private static ClinicSettingsCache? _instance;
    private readonly Dictionary<string, string> _settings = new()
    {
        ["MaxBookingsPerDay"] = "40",
        ["ReminderHoursBefore"] = "24",
    };

    private ClinicSettingsCache()
    {
    }

    public static ClinicSettingsCache Instance => _instance ??= new ClinicSettingsCache();

    public int GetInt(string key) => int.Parse(_settings[key]);
}
