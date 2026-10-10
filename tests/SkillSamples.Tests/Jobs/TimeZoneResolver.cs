using Hangfire;
using SkillSamples.Localization;

namespace SkillSamples.Jobs;

// Hangfire stores a recurring job's time zone as its id and looks the id up again (FindSystemTimeZoneById)
// each time it works out the next run: in the scheduler, the dashboard and AddOrUpdate. On an image without
// tzdata that lookup throws, so the zone passed in is lost. Registered in DI, this resolver is used in all three.
public sealed class TimeZoneResolver : ITimeZoneResolver
{
    public TimeZoneInfo GetTimeZoneById(string timeZoneId) =>
        timeZoneId == RiyadhTime.Zone.Id ? RiyadhTime.Zone : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
}
