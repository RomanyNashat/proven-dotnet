namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    /// <summary>"1012345678" becomes "******5678"; any other input keeps at most its last four characters.</summary>
    public static string NationalId(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "";
        }

        return trimmed.Length <= 4 ? new string('*', trimmed.Length) : new string('*', trimmed.Length - 4) + trimmed[^4..];
    }
}
