using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ClinicReminders.Infrastructure;

/// <summary>
/// Where a page ended: the last row's CreatedAt and Id. Rows can share a CreatedAt, so the Id is part of
/// the key, or a page edge between two such rows loses the rest of them. Opaque and URL-safe for clients.
/// </summary>
public sealed record ReminderCursor(DateTimeOffset CreatedAt, int Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{CreatedAt.UtcTicks}.{Id}")));

    /// <summary>No cursor (or an empty one) is the first page; anything unreadable is false.</summary>
    public static bool TryParse(string? value, out ReminderCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[64];
        // TryDecodeFromChars throws on characters outside the alphabet, so the input is checked first.
        if (value.Length > 64 || !Base64Url.IsValid(value) || !Base64Url.TryDecodeFromChars(value, bytes, out var written))
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(bytes[..written]);
        var dot = text.IndexOf('.');
        if (dot <= 0
            || !long.TryParse(text.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(text.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || id <= 0)
        {
            return false;
        }

        cursor = new ReminderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
