namespace ClinicQueue.Application;

/// <summary>One clinic's queue on one day, as the waiting-room screens see it.</summary>
public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);
