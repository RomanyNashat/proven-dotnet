using System.Text;

namespace HomeReadings.Domain;

/// <summary>Arabic keyboards and some phones send Arabic-Indic digits; stored values use ASCII digits.</summary>
public static class Digits
{
    /// <summary>Maps Arabic-Indic (U+0660-0669) and Persian (U+06F0-06F9) digits, and the Arabic decimal
    /// separator (U+066B), to ASCII. Every other character is kept as it is.</summary>
    /// <param name="text">The text to map.</param>
    /// <returns>The text with ASCII digits.</returns>
    public static string ToAscii(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                '٫' => '.',
                _ => ch,
            });
        }

        return builder.ToString();
    }
}
