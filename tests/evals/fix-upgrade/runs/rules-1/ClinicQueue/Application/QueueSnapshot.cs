namespace ClinicQueue.Application;

/// <summary>The queue positions of one clinic on one day.</summary>
/// <param name="ClinicId">The clinic.</param>
/// <param name="Day">The day of the queue.</param>
/// <param name="Positions">The booked positions, in order.</param>
public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);
