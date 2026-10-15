namespace Clinic.Api;

public interface IExport
{
    Task RunAsync();
}

[AutomaticRetry(Attempts = 0)]
public sealed class Export : IExport
{
    public Task RunAsync() => Task.CompletedTask;
}
