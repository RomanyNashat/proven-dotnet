using System.Text.RegularExpressions;

namespace Clinic.Api;

public static partial class Masking
{
    public static string NationalId(string value) => TenDigits().Replace(value, m => "******" + m.Value[^4..]);

    // \d, not [0-9]: in .NET it matches Arabic-Indic digits too.
    [GeneratedRegex(@"\d{10}")]
    private static partial Regex TenDigits();
}
