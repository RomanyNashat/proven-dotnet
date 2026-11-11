namespace MeterSync.Infrastructure;

public static class BaseAddress
{
    /// <summary>
    /// Parses a configured base address and makes it end with '/'. Without it, a relative path replaces the last
    /// segment: <c>https://meters.example.net/v2</c> + <c>meters/x</c> is <c>/meters/x</c>, not <c>/v2/meters/x</c>.
    /// </summary>
    /// <param name="configured">The configured absolute URL.</param>
    /// <returns>The absolute URI, ending with '/'.</returns>
    /// <exception cref="InvalidOperationException">The value is missing or not an absolute URL.</exception>
    public static Uri WithTrailingSlash(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)
            || !Uri.TryCreate(configured.EndsWith('/') ? configured : configured + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"A base address must be an absolute URL; got '{configured}'.");
        }

        return uri;
    }
}
