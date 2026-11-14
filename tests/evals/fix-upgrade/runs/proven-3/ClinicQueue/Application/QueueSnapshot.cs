namespace ClinicQueue.Application;

/// <summary>A clinic's queue for one day: the positions booked so far, in order.</summary>
/// <param name="ClinicId">The clinic.</param>
/// <param name="Day">The day of the queue.</param>
/// <param name="Positions">The booked positions, lowest first.</param>
public sealed record QueueSnapshot(int ClinicId, DateOnly Day, int[] Positions);
