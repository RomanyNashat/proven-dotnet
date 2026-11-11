namespace MeterSync.Infrastructure;

public static class BaseAddress
{
    /// <summary>
    /// Reads a base address and makes sure it ends in a slash. Without it, a relative path replaces the
    /// last segment: <c>https://meters.example.net/v2</c> + <c>meters/x</c> is <c>https://meters.example.net/meters/x</c>.
    /// </summary>
    /// <param name="value">The configured address.</param>
    /// <param name="key">The configuration key, for the error message.</param>
    /// <returns>An absolute URI ending in a slash.</returns>
    /// <exception cref="InvalidOperationException">The address is missing or not an absolute HTTPS URI.</exception>
    public static Uri From(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{key} must be an absolute https URI.");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
