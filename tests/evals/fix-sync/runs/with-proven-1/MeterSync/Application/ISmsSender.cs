namespace MeterSync.Application;

public interface ISmsSender
{
    /// <summary>Sends one SMS. Every call sends a message: the gateway has no idempotency key.</summary>
    Task SendAsync(string to, string text, CancellationToken ct);
}
