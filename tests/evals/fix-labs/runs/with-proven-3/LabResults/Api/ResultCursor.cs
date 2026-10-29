using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace LabResults.Api;

/// <summary>
/// Where a page ended: the last row's time AND id. Time alone loses the rows that share the last row's
/// timestamp (efcore-patterns §9). Sent to clients as an opaque string.
/// </summary>
public sealed record ResultCursor(DateTimeOffset At, int Id)
{
    /// <summary>The opaque form, passed back as <c>before</c>.</summary>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{At.UtcTicks}.{Id}")));

    /// <summary>Reads a cursor made by <see cref="Encode"/>.</summary>
    /// <param name="value">The <c>before</c> query value.</param>
    /// <param name="cursor">The cursor, its time in UTC (Npgsql takes only offset 0 for timestamptz).</param>
    /// <returns>Whether the value was a cursor.</returns>
    public static bool TryDecode(string? value, [NotNullWhen(true)] out ResultCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !Base64Url.IsValid(value))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value)).Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new ResultCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
