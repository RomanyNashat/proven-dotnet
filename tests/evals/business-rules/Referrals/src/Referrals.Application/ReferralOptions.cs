using System.ComponentModel.DataAnnotations;

namespace Referrals.Application;

public sealed class ReferralOptions
{
    public const string Section = "Referrals";

    // Kept from the first release, when every doctor had the same daily cap.
    public const int MaxDailyReferrals = 5;

    [Range(1, 20)]
    public int MaxPerPatientPerDay { get; init; }
}
