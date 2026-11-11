namespace MeterSync.Infrastructure;

public static class ServiceAddress
{
    /// <summary>
    /// The configured base address with a trailing slash. Without it, a relative path replaces the last segment:
    /// <c>https://meters.example.net/v2</c> + <c>meters/x</c> is <c>https://meters.example.net/meters/x</c>, which drops <c>/v2</c>.
    /// </summary>
    public static Uri From(string configured) =>
        new(configured.EndsWith('/') ? configured : configured + "/", UriKind.Absolute);
}
