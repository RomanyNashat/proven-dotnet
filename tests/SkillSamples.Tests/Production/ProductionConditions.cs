using System.Globalization;
using Xunit;

namespace SkillSamples.Production;

/// <summary>
/// Production tests run in a CI step that copies the conditions of a slim container image: no ICU
/// (invariant globalization), no time-zone database, UTC. Each one starts by checking the conditions are
/// really there, so a test can't pass in a friendlier environment by accident.
/// </summary>
public static class ProductionConditions
{
    public const string Trait = "Kind";
    public const string Production = "Production";
    public const string Story = "Story";

    /// <summary>An image with tzdata added but no ICU: the common Alpine half-fix. Its own CI step.</summary>
    public const string NoIcu = "NoIcu";

    public static void Require()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        Assert.Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("ar-SA"));
        Assert.Equal(TimeSpan.Zero, TimeZoneInfo.Local.BaseUtcOffset);
    }

    public static void RequireNoIcuWithTzdata()
    {
        Assert.Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("ar-SA"));
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
    }
}
