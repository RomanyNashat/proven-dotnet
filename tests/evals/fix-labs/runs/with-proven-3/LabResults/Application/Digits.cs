namespace LabResults.Application;

/// <summary>Arabic keyboards type Arabic-Indic digits; everything downstream expects 0-9.</summary>
public static class Digits
{
    /// <summary>Replaces Arabic-Indic (U+0660-0669) and Extended Arabic-Indic (U+06F0-06F9) digits with 0-9.</summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>The same text with Western digits.</returns>
    public static string ToWestern(string value) => string.Create(value.Length, value, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            };
        }
    });
}
