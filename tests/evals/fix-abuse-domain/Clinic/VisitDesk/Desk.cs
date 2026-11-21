namespace VisitDesk;

/// <summary>The clinic's visits: free slots, booking, and who may see or cancel what.</summary>
/// <remarks>The API calls one desk for many users at once; <c>callerId</c> is the signed-in user, from their token.</remarks>
public sealed class Desk(IVisitStore store, TimeProvider time)
{
    public const int MaxNoteLength = 500;

    /// <summary>The caller's own visits.</summary>
    public IReadOnlyList<Visit> Mine(int callerId) => store.VisitsOf(callerId);

    /// <summary>The clinic's slots that haven't started and have no booked visit.</summary>
    public IReadOnlyList<Slot> FreeSlots(int clinicId)
    {
        var now = time.GetUtcNow();
        return [.. store.SlotsOf(clinicId)
            .Where(s => s.StartsAt > now && !store.VisitsForSlot(s.Id).Any(v => v.Status == VisitStatus.Booked))
            .OrderBy(s => s.StartsAt)];
    }

    /// <summary>Books a visit. Not built yet: see task.md.</summary>
    public BookResult Book(int callerId, BookRequest request) => throw new NotImplementedException();

    /// <summary>One visit, or null. Not built yet.</summary>
    public Visit? Get(int callerId, int visitId) => throw new NotImplementedException();

    /// <summary>Cancels a visit and refunds it; false when it can't be. Not built yet.</summary>
    public bool Cancel(int callerId, int visitId) => throw new NotImplementedException();

    /// <summary>A patient's visits. Not built yet.</summary>
    public IReadOnlyList<Visit> ForPatient(int callerId, int patientId) => throw new NotImplementedException();
}
