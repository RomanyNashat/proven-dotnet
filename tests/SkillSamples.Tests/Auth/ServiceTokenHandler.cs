using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;

namespace SkillSamples.Auth;

public sealed record ServiceToken(string AccessToken, TimeSpan ExpiresIn);

/// <summary>Client credentials against the IdP's token endpoint.</summary>
public interface IServiceTokenClient
{
    Task<ServiceToken> RequestAsync(string audience, CancellationToken ct);
}

/// <summary>
/// Adds a client-credentials token to every outgoing call, cached until shortly before it expires (from the
/// token response, not a fixed guess). Attach it with the factory overload, one per audience:
/// <c>.AddHttpMessageHandler(sp => new ServiceTokenHandler(sp.GetRequiredService&lt;IServiceTokenClient&gt;(), sp.GetRequiredService&lt;IMemoryCache&gt;(), "inventory"))</c>.
/// </summary>
public sealed class ServiceTokenHandler(IServiceTokenClient tokens, IMemoryCache cache, string audience) : DelegatingHandler
{
    private static readonly TimeSpan RefreshEarly = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await cache.GetOrCreateAsync($"service-token:{audience}", async entry =>
        {
            var issued = await tokens.RequestAsync(audience, cancellationToken);
            entry.AbsoluteExpirationRelativeToNow = issued.ExpiresIn > RefreshEarly * 2 ? issued.ExpiresIn - RefreshEarly : issued.ExpiresIn / 2;
            return issued.AccessToken;
        });

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
