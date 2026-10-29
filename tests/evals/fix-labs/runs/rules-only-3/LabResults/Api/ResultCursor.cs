using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace LabResults.Api;

/// <summary>
/// Where the next page starts. Carries the id as well as the time, because two results can arrive in the
/// same microsecond and a time-only cursor would skip one of them.
/// </summary>
public sealed record ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    /// <summary>An opaque, URL-safe token for the <c>next</c> field.</summary>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.UtcTicks}:{Id}")));

    /// <summary>Reads a token made by <see cref="Encode"/>.</summary>
    /// <param name="value">The client's <c>before</c> value.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns>Whether the token is valid.</returns>
    public static bool TryParse(string? value, out ResultCursor cursor)
    {
        cursor = new ResultCursor(DateTimeOffset.MinValue, 0);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !Base64Url.IsValid(value))
        {
            return false;
        }

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
        {
            return false;
        }

        cursor = new ResultCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
