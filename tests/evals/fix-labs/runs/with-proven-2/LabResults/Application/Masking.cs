namespace LabResults.Application;

/// <summary>What may appear in a log line or an audit row about a patient.</summary>
public static class Masking
{
    /// <summary>The mask for a value too short to show any of it.</summary>
    public const string Full = "***MASKED***";

    /// <summary>
    /// Everything but the last four characters, whatever the format: "1012345678" becomes "******5678".
    /// Masking only a run of exactly ten digits let "1012 345 678" through unchanged.
    /// </summary>
    /// <param name="value">The national ID as the caller sent it.</param>
    /// <returns>The masked value, or <see cref="Full"/> when showing four characters would show half or more.</returns>
    public static string NationalId(string? value) => KeepLast(value, 4);

    private static string KeepLast(string? value, int visible) =>
        value is null || value.Length <= visible * 2 ? Full : new string('*', value.Length - visible) + value[^visible..];
}
