namespace Clinic.Rules;

/// <summary>When a repeat prescription may be refilled.</summary>
public static class RefillWindow
{
    public const int DaysBefore = 7;

    /// <summary>A refill may be asked for from <see cref="DaysBefore"/> days before the supply runs out, while refills are left.</summary>
    /// <param name="today">The pharmacy's date.</param>
    /// <param name="lastDispensed">The day the current supply was dispensed.</param>
    /// <param name="daysSupply">How many days the supply lasts.</param>
    /// <param name="refillsLeft">Refills left on the prescription.</param>
    /// <returns>Whether a refill may be requested today.</returns>
    public static bool IsOpen(DateOnly today, DateOnly lastDispensed, int daysSupply, int refillsLeft)
    {
        if (refillsLeft <= 0)
            return false;
        var runsOut = lastDispensed.AddDays(daysSupply);
        return today >= runsOut.AddDays(-DaysBefore);
    }
}
