using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace MeterSync.Tests;

public sealed class RecordingHandler(HttpStatusCode status, string body = "[]") : HttpMessageHandler
{
    public ConcurrentQueue<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
