using System.Net;

namespace MeterSync.Tests;

/// <summary>Answers every request with a fixed response and records what was asked.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string Body { get; set; } = "[]";

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(new HttpResponseMessage(Status)
        {
            Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
