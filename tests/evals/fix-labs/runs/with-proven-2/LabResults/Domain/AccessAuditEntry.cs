namespace LabResults.Domain;

/// <summary>Who read patient data, which action, and when. Append-only.</summary>
public sealed class AccessAuditEntry
{
    public const string ListResults = "results.list";
    public const string LookupOrders = "lab-orders.lookup";

    private AccessAuditEntry()
    {
    }

    public long Id { get; private set; }

    public string UserId { get; private set; } = "";

    public string Action { get; private set; } = "";

    /// <summary>
    /// The patient reference: for a lookup the masked national ID (last four digits), so the audit doesn't
    /// become a second copy of patient identifiers.
    /// </summary>
    public string? PatientRef { get; private set; }

    public int ItemCount { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Records one read.</summary>
    /// <param name="userId">The caller's <c>sub</c>.</param>
    /// <param name="action">One of the action constants.</param>
    /// <param name="patientRef">A masked patient reference, or null.</param>
    /// <param name="itemCount">How many records were returned.</param>
    /// <param name="at">From the injected <see cref="TimeProvider"/>.</param>
    /// <returns>The audit row to add.</returns>
    public static AccessAuditEntry Record(string userId, string action, string? patientRef, int itemCount, DateTimeOffset at) =>
        new() { UserId = userId, Action = action, PatientRef = patientRef, ItemCount = itemCount, At = at };
}
