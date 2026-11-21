namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    /// <summary>
    /// "1012345678" becomes "******5678". Whatever the input looks like, at most the last four characters survive,
    /// and short values are masked completely.
    /// </summary>
    public static string NationalId(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
    }
}
