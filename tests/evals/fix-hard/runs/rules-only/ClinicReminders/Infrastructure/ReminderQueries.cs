using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed record ReminderRow(int Id, DateTimeOffset AppointmentAt, DateTimeOffset CreatedAt, bool Sent);

public sealed record ReminderPage(IReadOnlyList<ReminderRow> Items, string? Next);

public sealed class ReminderQueries(RemindersDbContext db)
{
    /// <summary>Reads a page cursor. No cursor (or an empty one) means the first page.</summary>
    /// <param name="cursor">The cursor from a previous page's <c>next</c>.</param>
    /// <param name="afterId">The last id of the previous page, or null for the first page.</param>
    /// <returns>False when the cursor is not one this API issued.</returns>
    public static bool TryParseCursor(string? cursor, out int? afterId)
    {
        afterId = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        if (int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            afterId = id;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The page after <paramref name="afterId"/>, oldest first, for the reminders screen. <c>Next</c> is null on
    /// the last page. Keyed on the id, which is unique: many reminders can share a CreatedAt, and a cursor on
    /// CreatedAt alone skipped the rest of them.
    /// </summary>
    /// <param name="patientId">The patient whose reminders to read.</param>
    /// <param name="afterId">The last id of the previous page, or null for the first page.</param>
    /// <param name="size">The page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page.</returns>
    public async Task<ReminderPage> PageAsync(int patientId, int? afterId, int size, CancellationToken ct)
    {
        var reminders = db.Reminders.AsNoTracking().Where(r => r.PatientId == patientId);
        if (afterId is int after)
        {
            reminders = reminders.Where(r => r.Id > after);
        }

        // One extra row tells us whether another page exists, so a full last page doesn't hand out a cursor
        // to an empty page.
        var items = await reminders
            .OrderBy(r => r.Id)
            .Take(size + 1)
            .Select(r => new ReminderRow(r.Id, r.AppointmentAt, r.CreatedAt, r.SentAt != null))
            .ToListAsync(ct);

        string? next = null;
        if (items.Count > size)
        {
            items.RemoveAt(size);
            next = items[^1].Id.ToString(CultureInfo.InvariantCulture);
        }

        return new ReminderPage(items, next);
    }
}
