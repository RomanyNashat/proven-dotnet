using System.Globalization;

namespace HomeReadings.Application;

/// <summary>Parses the value as the meter shows it ("7.8", "142"), whatever language the app is in.</summary>
public static class MeterValue
{
    private const int MaxLength = 12;

    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return false;
        }

        // Always the invariant culture: under ar-SA the decimal separator is "٫" and "7.8" fails;
        // under en-US "7,8" would quietly become 78. Arabic-Indic digits are accepted in case the app
        // shows the value in Arabic.
        Span<char> buffer = stackalloc char[MaxLength];
        var source = text.AsSpan().Trim();
        for (var i = 0; i < source.Length; i++)
        {
            buffer[i] = source[i] switch
            {
                >= '٠' and <= '٩' => (char)('0' + (source[i] - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (source[i] - '۰')),
                '٫' => '.',
                var c => c,
            };
        }

        return decimal.TryParse(buffer[..source.Length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
