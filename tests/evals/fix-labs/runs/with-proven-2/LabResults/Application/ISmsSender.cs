namespace LabResults.Application;

/// <summary>The SMS gateway, as the application sees it.</summary>
public interface ISmsSender
{
    /// <summary>Sends one message; throws when the gateway refuses or can't be reached.</summary>
    /// <param name="to">The recipient in E.164 form.</param>
    /// <param name="text">The message text.</param>
    /// <param name="ct">Cancels the call.</param>
    Task SendAsync(string to, string text, CancellationToken ct);
}
