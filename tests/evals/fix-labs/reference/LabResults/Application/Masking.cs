using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static partial class Masking
{
    /// <summary>"1012345678" becomes "******5678".</summary>
    public static string NationalId(string value) => TenDigits().Replace(value, m => "******" + m.Value[^4..]);

    [GeneratedRegex(@"\p{Nd}{10}")]   // any digits: Arabic keyboards type ١٠١٢...
    private static partial Regex TenDigits();
}
