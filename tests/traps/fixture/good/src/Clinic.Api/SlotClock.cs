namespace Clinic.Api;

public sealed class SlotClock(TimeProvider time)
{
    public DateTimeOffset Now => time.GetUtcNow();
}
