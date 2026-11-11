namespace MeterSync.Infrastructure;

public static class HttpBaseAddress
{
    /// <summary>
    /// The configured base address with a trailing slash. Without it, a relative path replaces the last
    /// segment: <c>https://host/v2</c> + <c>meters/X</c> resolves to <c>https://host/meters/X</c>.
    /// </summary>
    /// <param name="configured">An absolute URL from configuration.</param>
    /// <returns>The base address, ending in exactly one slash.</returns>
    /// <exception cref="InvalidOperationException">The value is missing or not an absolute URL.</exception>
    public static Uri From(string? configured) =>
        Uri.TryCreate(configured?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(configured)
            ? uri
            : throw new InvalidOperationException($"'{configured}' is not an absolute base address.");
}
