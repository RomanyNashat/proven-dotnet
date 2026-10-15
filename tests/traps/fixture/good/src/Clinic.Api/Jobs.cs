namespace Clinic.Api;

public interface IExport
{
    [AutomaticRetry(Attempts = 0)]
    Task RunAsync();
}

public sealed class Export : IExport
{
    public Task RunAsync() => Task.CompletedTask;
}
