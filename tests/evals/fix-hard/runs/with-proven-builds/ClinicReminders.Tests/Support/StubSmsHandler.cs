using System.Net;

namespace ClinicReminders.Tests.Support;

/// <summary>Answers SMS calls from a script and records what was sent.</summary>
public sealed class StubSmsHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(Uri Uri, string Body)> Requests { get; } = [];

    public static StubSmsHandler Answering(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    public static StubSmsHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, body));
        return await respond(request, cancellationToken);
    }
}
