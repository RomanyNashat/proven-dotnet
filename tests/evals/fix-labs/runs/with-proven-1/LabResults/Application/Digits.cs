namespace LabResults.Application;

/// <summary>Arabic keyboards type Arabic-Indic (U+0660) or Extended (U+06F0) digits; both become ASCII.</summary>
internal static class Digits
{
    public static string ToAscii(string value) => string.Create(value.Length, value, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = source[i] switch
            {
                >= '٠' and <= '٩' and var c => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' and var c => (char)('0' + (c - '۰')),
                var c => c,
            };
        }
    });

    public static bool AreAllAscii(ReadOnlySpan<char> value) => value.Length > 0 && !value.ContainsAnyExceptInRange('0', '9');
}
