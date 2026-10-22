using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace HomeReadings.Infrastructure;

/// <summary>The care team's system. It has no idempotency key: every POST is a new alert to a nurse.</summary>
public sealed class CareTeamClient(HttpClient http)
{
    public async Task SendAlertAsync(string nationalId, decimal value, DateTime collectedAt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("alerts", new { nationalId, value, collectedAt }, ct);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class CareTeamOptions
{
    public const string Section = "CareTeam";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The base address with a trailing '/', or "alerts" would replace its last path segment.</summary>
    public Uri RequiredBaseAddress()
    {
        var address = BaseAddress ?? throw new InvalidOperationException("CareTeam:BaseAddress is not set.");
        return address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
    }
}
