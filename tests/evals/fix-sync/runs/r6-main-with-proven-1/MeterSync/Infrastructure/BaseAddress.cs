namespace MeterSync.Infrastructure;

/// <summary>Turns a configured base URL into an <see cref="HttpClient.BaseAddress"/>.</summary>
public static class BaseAddress
{
    /// <summary>
    /// Returns the URL with a trailing slash. Without it, a relative path such as <c>meters/x</c> replaces the
    /// last segment, so <c>https://host/v2</c> would resolve to <c>https://host/meters/x</c>.
    /// </summary>
    /// <param name="configured">The configured URL.</param>
    /// <param name="settingName">The setting's key, named in the error.</param>
    /// <returns>An absolute URI ending with <c>/</c>.</returns>
    /// <exception cref="InvalidOperationException">The setting is missing or not an absolute URL.</exception>
    public static Uri WithTrailingSlash(string? configured, string settingName)
    {
        if (string.IsNullOrWhiteSpace(configured) || !Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute URL.");
        }

        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
