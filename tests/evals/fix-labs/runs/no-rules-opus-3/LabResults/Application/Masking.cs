namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static class Masking
{
    /// <summary>"1012345678" becomes "******5678"; anything that is not exactly ten digits is masked entirely.</summary>
    public static string NationalId(string? value) =>
        value is { Length: 10 } && value.All(char.IsAsciiDigit) ? "******" + value[^4..] : "***";
}
