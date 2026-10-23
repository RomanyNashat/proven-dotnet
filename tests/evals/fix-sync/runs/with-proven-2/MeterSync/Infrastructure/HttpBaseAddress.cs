namespace MeterSync.Infrastructure;

public static class HttpBaseAddress
{
    /// <summary>
    /// A base address keeps its path only when it ends with '/': "https://meters.example.net/v2" plus
    /// "meters/x/readings" is "https://meters.example.net/meters/x/readings", which the vendor answers with a
    /// 404. Production's setting has no trailing slash, so it's added here rather than refused.
    /// </summary>
    /// <param name="configured">The configured base address.</param>
    /// <returns>The same address, ending with '/'.</returns>
    public static Uri WithTrailingSlash(Uri configured) =>
        configured.AbsolutePath.EndsWith('/') ? configured : new UriBuilder(configured) { Path = configured.AbsolutePath + "/" }.Uri;
}
