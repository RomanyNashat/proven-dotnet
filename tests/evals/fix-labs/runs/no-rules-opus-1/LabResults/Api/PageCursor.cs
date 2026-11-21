using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace LabResults.Api;

/// <summary>
/// Opaque keyset cursor for <c>GET /results</c>: the (ReceivedAt, Id) of the last item on a page.
/// Including the id means results received in the same instant are neither skipped nor repeated.
/// </summary>
public readonly record struct PageCursor(DateTimeOffset ReceivedAt, int Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.UtcTicks}:{Id}")));

    public static bool TryDecode(string? value, out PageCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = text.Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new PageCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
