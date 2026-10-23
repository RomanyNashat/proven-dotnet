namespace MeterSync.Infrastructure;

/// <summary>Reads an HTTP client's base address from configuration.</summary>
public static class BaseAddress
{
    /// <summary>
    /// The address as an absolute URI ending in "/". Without the slash a relative request path replaces
    /// the last segment: "meters/x" against "https://host/v2" goes to "https://host/meters/x" and drops v2.
    /// </summary>
    /// <param name="value">The configured address.</param>
    /// <param name="key">The configuration key, for the error message.</param>
    /// <returns>The base address.</returns>
    /// <exception cref="InvalidOperationException">The setting is missing or isn't an absolute https URI.</exception>
    public static Uri From(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{key} must be an absolute https address.");
        }

        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
