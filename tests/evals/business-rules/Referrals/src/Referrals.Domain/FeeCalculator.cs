namespace Referrals.Domain;

/// <summary>What a patient pays for a specialist visit.</summary>
public static class FeeCalculator
{
    public const decimal BaseFee = 150m;

    public static decimal ForVisit(int visitsThisYear) =>
        visitsThisYear >= 5 ? Math.Round(BaseFee * 0.9m, 2) : BaseFee;
}
