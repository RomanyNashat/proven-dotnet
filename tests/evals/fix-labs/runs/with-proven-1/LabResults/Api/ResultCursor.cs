using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace LabResults.Api;

/// <summary>
/// The opaque <c>next</c>/<c>before</c> value: the last row's time and id, so rows that share a timestamp
/// are neither skipped nor repeated.
/// </summary>
public sealed record ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    /// <summary>The cursor as a URL-safe string.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.UtcTicks}.{Id}")));

    /// <summary>Reads a cursor made by <see cref="Encode"/>; anything else is refused.</summary>
    /// <param name="value">The <c>before</c> query value.</param>
    /// <param name="cursor">The cursor, in UTC (Npgsql takes only offset 0 for <c>timestamptz</c>).</param>
    /// <returns>Whether <paramref name="value"/> is a valid cursor.</returns>
    public static bool TryDecode(string value, out ResultCursor cursor)
    {
        cursor = null!;
        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(bytes).Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new ResultCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
