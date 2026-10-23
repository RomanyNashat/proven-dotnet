namespace MeterSync.Infrastructure;

/// <summary>Reads an HTTP client's base address from configuration.</summary>
public static class HttpBaseAddress
{
    /// <summary>
    /// Returns the configured base address with a trailing slash. Without it, a relative path such as
    /// <c>meters/x/readings</c> replaces the last segment: <c>https://host/v2</c> becomes
    /// <c>https://host/meters/x/readings</c> and the <c>v2</c> is lost.
    /// </summary>
    /// <param name="configuration">The app configuration.</param>
    /// <param name="key">The setting, for example <c>MeterVendor:BaseAddress</c>.</param>
    /// <returns>An absolute URI ending in <c>/</c>.</returns>
    /// <exception cref="InvalidOperationException">The setting is missing or not an absolute URI.</exception>
    public static Uri From(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        // On Linux "/path" parses as an absolute file:// URI, so the scheme is checked too.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"Setting '{key}' must be an absolute http(s) URI.");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
