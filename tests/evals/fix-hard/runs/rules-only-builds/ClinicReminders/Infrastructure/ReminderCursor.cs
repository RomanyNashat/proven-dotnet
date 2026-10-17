using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ClinicReminders.Infrastructure;

/// <summary>The position after the last row of a page. Opaque to clients: they pass back the <c>next</c> they were given.</summary>
public readonly record struct ReminderCursor(DateTimeOffset CreatedAt, int Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{CreatedAt.UtcTicks}.{Id}")));

    /// <summary>Reads a cursor a client sent back. False for anything this service did not produce.</summary>
    public static bool TryDecode(string text, out ReminderCursor cursor)
    {
        cursor = default;
        if (text.Length > 64 || !Base64Url.IsValid(text))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(text)).Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new ReminderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
