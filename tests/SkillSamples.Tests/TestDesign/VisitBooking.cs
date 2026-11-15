namespace SkillSamples.TestDesign;

public sealed record Person(int Id, DateOnly BirthDate);

public enum BookingDecision { Allowed, TooSoon, TooFarAhead, NeedsGuardian, PatientBooksThemself, NotYourDependent }

public enum VisitStatus { Booked, CheckedIn, Completed, Cancelled }

/// <summary>
/// Who may book a visit, and when. The rules of the story in <c>story.md</c>, which the tests trace with
/// <c>[Trait("AC", ...)]</c>.
/// </summary>
public static class VisitBooking
{
    /// <summary>Saudi Arabia keeps UTC+3 all year, so the clinic's date needs no time-zone database.</summary>
    public static readonly TimeSpan ClinicOffset = TimeSpan.FromHours(3);
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(2);
    public static readonly TimeSpan MaximumAhead = TimeSpan.FromDays(30);
    public const int AdultAge = 18;

    private static readonly HashSet<(VisitStatus From, VisitStatus To)> Moves =
    [
        (VisitStatus.Booked, VisitStatus.CheckedIn),
        (VisitStatus.CheckedIn, VisitStatus.Completed),
        (VisitStatus.Booked, VisitStatus.Cancelled),
    ];

    /// <summary>Decides whether <paramref name="booker"/> may book <paramref name="patient"/> into a slot.</summary>
    /// <param name="booker">The person making the booking.</param>
    /// <param name="patient">The person the visit is for.</param>
    /// <param name="isLinkedGuardian">Whether the booker is the patient's linked guardian.</param>
    /// <param name="slotStart">When the visit starts.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The decision; <see cref="BookingDecision.Allowed"/> books the visit.</returns>
    public static BookingDecision Decide(Person booker, Person patient, bool isLinkedGuardian, DateTimeOffset slotStart, DateTimeOffset now)
    {
        var notice = slotStart - now;
        if (notice < MinimumNotice)
            return BookingDecision.TooSoon;
        if (notice > MaximumAhead)
            return BookingDecision.TooFarAhead;

        // The age that counts is the patient's on the day of the visit at the clinic, not in UTC.
        var adult = AgeOn(patient.BirthDate, ClinicDate(slotStart)) >= AdultAge;
        return (booker.Id == patient.Id, isLinkedGuardian, adult) switch
        {
            (true, _, true) => BookingDecision.Allowed,
            (true, _, false) => BookingDecision.NeedsGuardian,
            (false, false, _) => BookingDecision.NotYourDependent,
            (false, true, false) => BookingDecision.Allowed,
            (false, true, true) => BookingDecision.PatientBooksThemself,
        };
    }

    /// <summary>Whether a visit may move from one status to another.</summary>
    /// <param name="from">The visit's status now.</param>
    /// <param name="to">The status it would move to.</param>
    /// <returns><c>true</c> for Booked to CheckedIn to Completed, and Booked to Cancelled.</returns>
    public static bool CanMove(VisitStatus from, VisitStatus to) => Moves.Contains((from, to));

    /// <summary>The date at the clinic when a moment happens.</summary>
    /// <param name="moment">Any moment, in any offset.</param>
    /// <returns>The calendar date in Riyadh.</returns>
    public static DateOnly ClinicDate(DateTimeOffset moment) => DateOnly.FromDateTime(moment.ToOffset(ClinicOffset).DateTime);

    /// <summary>A person's age in whole years on a date.</summary>
    /// <param name="birth">The date of birth.</param>
    /// <param name="day">The date to count to.</param>
    /// <returns>Completed years.</returns>
    public static int AgeOn(DateOnly birth, DateOnly day)
    {
        var age = day.Year - birth.Year;
        return birth > day.AddYears(-age) ? age - 1 : age;
    }
}
