using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Localization;

/// <summary>
/// A Windows time-zone id ("Arab Standard Time") works on Linux only through ICU, which maps it to the IANA
/// id. On an image with tzdata but no ICU the IANA id works and the Windows one doesn't.
/// </summary>
public sealed class TimeZoneIdTests
{
    [Fact]
    public void WithIcuAndTzdata_BothIdsFindRiyadh()
    {
        Assert.Equal(TimeSpan.FromHours(3), TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh").BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(3), TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time").BaseUtcOffset);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.NoIcu)]
    public void TzdataButNoIcu_TheIanaIdWorks_TheWindowsIdIsNotFound()
    {
        ProductionConditions.RequireNoIcuWithTzdata();

        Assert.Equal(TimeSpan.FromHours(3), TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh").BaseUtcOffset);
        Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time"));
    }
}
