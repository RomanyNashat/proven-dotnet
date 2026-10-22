namespace HomeReadings.Application;

/// <summary>The request is well-formed but not acceptable; the message is safe to show the patient.</summary>
public sealed class RequestRejectedException(string message) : Exception(message);
