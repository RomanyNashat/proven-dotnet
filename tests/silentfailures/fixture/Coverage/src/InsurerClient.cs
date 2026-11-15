namespace Coverage;

public sealed class InsurerClient(HttpClient http, ILogger<InsurerClient> logger)
{
    public async Task<decimal> CoveredPercentAsync(int patientId, CancellationToken ct)
    {
        try
        {
            var answer = await http.GetFromJsonAsync<CoverageAnswer>($"coverage/{patientId}", ct);
            return answer!.Percent;
        }
        catch (HttpRequestException)
        {
            return 0m;
        }
    }

    public async Task NotifyAsync(int patientId, CancellationToken ct)
    {
        try
        {
            await http.PostAsync($"notify/{patientId}", null, ct);
        }
        catch (Exception) { }
    }

    public async Task<CoverageAnswer?> TryCoverageAsync(int patientId, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<CoverageAnswer>($"coverage/{patientId}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "The insurer is down; the caller retries.");
            throw new InsurerUnavailableException(patientId, ex);
        }
    }
}
