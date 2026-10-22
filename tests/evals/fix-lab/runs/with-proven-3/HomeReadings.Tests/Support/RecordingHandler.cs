using System.Net;

namespace HomeReadings.Tests.Support;

/// <summary>Stands in for the care team's system: records each request and answers as told.</summary>
public sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    public List<string> Bodies { get; } = [];

    public static RecordingHandler Answering(HttpStatusCode status) => new(_ => new HttpResponseMessage(status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Bodies)
            {
                Bodies.Add(body);
            }
        }

        return respond(request);
    }
}
