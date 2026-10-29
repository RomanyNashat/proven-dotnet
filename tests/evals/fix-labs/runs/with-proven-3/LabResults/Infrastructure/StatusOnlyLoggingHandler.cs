namespace LabResults.Infrastructure;

/// <summary>
/// Replaces <c>IHttpClientFactory</c>'s default logging on clients whose URLs and errors carry patient data
/// (the national ID is in the lab's path, the phone in the SMS body). It logs the client, the status code and
/// the exception type: never the URI, the message or the exception itself.
/// </summary>
public sealed class StatusOnlyLoggingHandler(string client, ILogger<StatusOnlyLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Client} {Method} answered {StatusCode}", client, request.Method.Method, (int)response.StatusCode);
            }

            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("{Client} {Method} failed: {ExceptionType}", client, request.Method.Method, ex.GetType().Name);
            throw;
        }
    }
}
