using System.Text.RegularExpressions;

namespace Clinic.Api;

public static partial class Masking
{
    public static string NationalId(string value) => TenDigits().Replace(value, m => "******" + m.Value[^4..]);

    [GeneratedRegex("[0-9]{10}")]
    private static partial Regex TenDigits();
}
