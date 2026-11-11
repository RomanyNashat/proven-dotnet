namespace MeterSync.Infrastructure;

public static class BaseAddress
{
    /// <summary>
    /// The configured base address with a trailing slash. Without it, a relative request URI replaces
    /// the last segment: <c>https://meters.example.net/v2</c> + <c>meters/x</c> goes to <c>/meters/x</c>, not <c>/v2/meters/x</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is missing or isn't an absolute URL.</exception>
    public static Uri WithTrailingSlash(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || !Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"A base address must be an absolute URL, but was '{configured}'.");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
