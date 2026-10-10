using SkillSamples.Localization;

namespace SkillSamples.Core;

// Time comes from an injected TimeProvider, so a test can move the clock instead of waiting.
public sealed class SlotHold
{
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(15);

    private SlotHold(int slotId, DateTimeOffset expiresAt) => (SlotId, ExpiresAt) = (slotId, expiresAt);

    public int SlotId { get; }
    public DateTimeOffset ExpiresAt { get; }   // stored and compared in UTC

    // What the patient sees. RiyadhTime works on images without tzdata (`localization` §7).
    public DateTimeOffset ExpiresAtInRiyadh => TimeZoneInfo.ConvertTime(ExpiresAt, RiyadhTime.Zone);

    public static SlotHold Place(int slotId, TimeProvider time) => new(slotId, time.GetUtcNow() + HoldFor);

    public bool HasLapsed(TimeProvider time) => time.GetUtcNow() >= ExpiresAt;
}
