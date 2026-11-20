namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    /// <summary>
    /// Keeps only the last four characters: "1012345678" becomes "******5678", "+966512345678" becomes "******5678".
    /// Anything of eight characters or fewer is masked completely, so nothing short enough to guess leaks.
    /// </summary>
    public static string Identifier(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Length <= 8 ? "******" : "******" + value[^4..];
    }
}
