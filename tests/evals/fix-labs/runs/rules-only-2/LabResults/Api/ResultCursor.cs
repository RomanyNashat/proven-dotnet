using System.Globalization;

namespace LabResults.Api;

/// <summary>
/// Where a page of results ends: the last item's time and id. The id breaks ties, so results recorded in the same
/// microsecond are neither repeated nor skipped.
/// </summary>
public readonly record struct ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    private const char Separator = '_';

    /// <summary>The cursor as the opaque <c>next</c> string a client passes back as <c>before</c>.</summary>
    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{ReceivedAt.UtcTicks}{Separator}{Id}");

    /// <summary>Reads a cursor made by <see cref="Encode"/>.</summary>
    /// <returns>False for anything else.</returns>
    public static bool TryParse(string? value, out ResultCursor cursor)
    {
        cursor = default;
        var parts = value?.Split(Separator);
        if (parts is not [var ticksText, var idText]
            || !long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new ResultCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
