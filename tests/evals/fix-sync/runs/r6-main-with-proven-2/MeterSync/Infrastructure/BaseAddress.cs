namespace MeterSync.Infrastructure;

public static class BaseAddress
{
    /// <summary>
    /// A base address without a trailing slash loses its last segment when a relative path is resolved
    /// against it (<c>.../v2</c> + <c>meters/x</c> is <c>.../meters/x</c>), so one is always added.
    /// </summary>
    public static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
