namespace LabResults.Application;

/// <summary>Sends the SMS waiting in the outbox.</summary>
public interface ISmsRelay
{
    /// <summary>Sends one batch of pending messages.</summary>
    /// <param name="ct">Stops the batch on shutdown.</param>
    /// <returns>How many messages were sent.</returns>
    Task<int> RelayPendingAsync(CancellationToken ct);
}
