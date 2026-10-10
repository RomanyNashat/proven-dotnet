using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Compliance;

// Where personal data is stored is decided by configuration, so check it where configuration is read:
// a storage region outside the allowed list stops the service at start-up.
public sealed class DataResidencyOptions
{
    public const string Section = "DataResidency";

    public string StorageRegion { get; init; } = "";
    public string[] AllowedRegions { get; init; } = [];
}

public static class DataResidencySetup
{
    public static IServiceCollection AddDataResidencyCheck(this IServiceCollection services)
    {
        services.AddOptions<DataResidencyOptions>()
            .BindConfiguration(DataResidencyOptions.Section)
            .Validate(o => o.AllowedRegions.Contains(o.StorageRegion, StringComparer.OrdinalIgnoreCase),
                "DataResidency:StorageRegion must be one of DataResidency:AllowedRegions.")
            .ValidateOnStart();
        return services;
    }
}
