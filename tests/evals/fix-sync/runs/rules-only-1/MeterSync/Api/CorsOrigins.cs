namespace MeterSync.Api;

/// <summary>The browser origins allowed to call the API.</summary>
public static class CorsOrigins
{
    /// <summary>
    /// Reads <c>Cors:Origins</c> either as a list (<c>Cors:Origins:0</c>, ...) or as one value, which may
    /// hold several origins separated by commas (how an environment variable usually carries it).
    /// </summary>
    /// <param name="configuration">The app configuration.</param>
    /// <returns>The distinct origins; empty when none are configured.</returns>
    public static string[] From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Cors:Origins");
        IEnumerable<string?> values = section.Value is { } single
            ? single.Split([',', ';'])
            : section.GetChildren().Select(c => c.Value);

        return values
            .Select(v => v?.Trim().TrimEnd('/'))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
