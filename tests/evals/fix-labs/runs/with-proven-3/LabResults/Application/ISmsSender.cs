namespace LabResults.Application;

/// <summary>Sends one SMS. Implemented by the gateway client in Infrastructure.</summary>
public interface ISmsSender
{
    /// <summary>Sends <paramref name="text"/> to <paramref name="to"/>.</summary>
    /// <param name="to">The number, in E.164 form.</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when the gateway accepted the message.</returns>
    Task SendAsync(string to, string text, CancellationToken ct);
}
