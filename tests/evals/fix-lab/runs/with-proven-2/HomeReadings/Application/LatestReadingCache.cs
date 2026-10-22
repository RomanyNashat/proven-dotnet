using System.Globalization;

namespace HomeReadings.Application;

/// <summary>One cache entry per patient: a shared key served one patient's reading to every other patient.</summary>
public static class LatestReadingCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    public static string Key(int patientId) => string.Create(CultureInfo.InvariantCulture, $"readings:v1:latest:{patientId}");
}
