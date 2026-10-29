using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace LabResults.Application;

/// <summary>
/// Keyset position in the newest-first result list. The id breaks ties: results recorded in the same
/// instant would otherwise be skipped or repeated across pages.
/// </summary>
/// <param name="ReceivedAt">When the last result on the page was received.</param>
/// <param name="Id">The id of the last result on the page.</param>
public sealed record ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    /// <summary>Opaque URL-safe form handed to the client as <c>next</c>.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.UtcTicks}:{Id}")));

    /// <summary>Reads a cursor produced by <see cref="Encode"/>.</summary>
    /// <param name="value">The <c>before</c> value from the client.</param>
    /// <param name="cursor">The cursor, in UTC, when parsing succeeds.</param>
    /// <returns><c>true</c> when <paramref name="value"/> is a valid cursor.</returns>
    public static bool TryParse(string? value, out ResultCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !Base64Url.IsValid(value))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value)).Split(':');
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
