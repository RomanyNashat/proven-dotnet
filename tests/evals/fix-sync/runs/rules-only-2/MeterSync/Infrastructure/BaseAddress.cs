namespace MeterSync.Infrastructure;

public static class BaseAddress
{
    /// <summary>
    /// Makes a configured base address safe for relative paths. Without the trailing slash,
    /// <c>https://meters.example.net/v2</c> + <c>meters/x/readings</c> resolves to
    /// <c>https://meters.example.net/meters/x/readings</c>: the <c>v2</c> segment is dropped.
    /// </summary>
    /// <param name="configured">The configured address; must be absolute.</param>
    /// <param name="setting">The setting's name, for the error message.</param>
    /// <returns>The address, ending in a slash.</returns>
    /// <exception cref="InvalidOperationException">The setting is missing or not an absolute URI.</exception>
    public static Uri From(string? configured, string setting)
    {
        if (string.IsNullOrWhiteSpace(configured) || !Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{setting} must be an absolute URI.");
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
