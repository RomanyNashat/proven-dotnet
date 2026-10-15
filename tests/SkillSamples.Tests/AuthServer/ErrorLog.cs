using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace SkillSamples.AuthServer;

// The server's errors from a FakeLogCollector, so a failed test can say what went wrong inside it.
public static class ErrorLog
{
    public static IReadOnlyList<string> Errors(this FakeLogCollector logs) =>
        [.. logs.GetSnapshot().Where(r => r.Level >= LogLevel.Error).Select(r => $"{r.Category}: {r.Message} {r.Exception}")];

    public static string ErrorText(this FakeLogCollector logs) => string.Join(Environment.NewLine, logs.Errors());
}
