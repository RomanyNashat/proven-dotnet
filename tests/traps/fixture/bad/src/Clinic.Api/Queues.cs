namespace Clinic.Api;

public static class Queues
{
    public static readonly Dictionary<string, object?> Visits = new() { ["x-queue-type"] = "quorum" };
}
