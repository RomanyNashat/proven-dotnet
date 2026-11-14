namespace ClinicQueue.Application;

/// <summary>A clinic's queue for one day: the positions booked, in order.</summary>
public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);
