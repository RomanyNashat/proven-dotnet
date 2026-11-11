namespace MeterSync.Infrastructure;

/// <summary>Builds an <see cref="HttpClient.BaseAddress"/> that keeps its path.</summary>
public static class HttpBaseAddress
{
    /// <summary>
    /// Adds the trailing slash a base address needs: without it, a relative request URI replaces the last
    /// segment, so <c>https://meters.example.net/v2</c> + <c>meters/x</c> becomes <c>https://meters.example.net/meters/x</c>.
    /// </summary>
    /// <param name="address">An absolute https URL from configuration.</param>
    /// <returns>The base address.</returns>
    /// <exception cref="InvalidOperationException">The address isn't an absolute URL.</exception>
    public static Uri From(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.EndsWith('/') ? address : address + "/", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"'{address}' isn't an absolute base address.");
        }

        return uri;
    }
}
