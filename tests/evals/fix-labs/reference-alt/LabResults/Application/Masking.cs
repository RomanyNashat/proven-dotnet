using System.Text.RegularExpressions;

namespace LabResults.Application;

/// <summary>What may appear in a log line about a patient.</summary>
public static partial class Masking
{
    /// <summary>"1012345678" becomes "******5678".</summary>
    public static string NationalId(string value) => TenDigits().Replace(WesternDigits(value), m => "******" + m.Value[^4..]);

    // Arabic keyboards type Arabic-Indic digits (١٠١٢...): turn every decimal digit into 0-9 before matching.
    private static string WesternDigits(string value) =>
        string.Create(value.Length, value, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsDigit(source[i]) ? (char)('0' + (int)char.GetNumericValue(source[i])) : source[i];
            }
        });

    [GeneratedRegex("[0-9]{10}")]
    private static partial Regex TenDigits();
}
