using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ClinicReminders.Infrastructure;

/// <summary>
/// The last row of a page: its CreatedAt and its Id. CreatedAt alone isn't unique, and a cursor of "later
/// than the last row" would skip the rest of that row's ties. Encoded as "{UTC ticks}_{id}": invariant digits,
/// and nothing that needs escaping in a query string (an ISO date's "+" becomes a space).
/// </summary>
public sealed record ReminderCursor(DateTimeOffset CreatedAt, int Id)
{
    private const char Separator = '_';

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{CreatedAt.UtcTicks}{Separator}{Id}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out ReminderCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(Separator);
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        cursor = new ReminderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
