namespace LabResults.Infrastructure;

/// <summary>Settings the service cannot run without; checked at startup so a bad deploy fails fast.</summary>
public sealed class RequiredSettings
{
    public string? LabConnectionString { get; set; }

    public string? LabsBaseAddress { get; set; }

    public string? SmsBaseAddress { get; set; }

    public string? AuthAuthority { get; set; }

    /// <summary>Reads the settings from configuration.</summary>
    /// <param name="settings">The instance to fill.</param>
    /// <param name="configuration">App configuration.</param>
    public static void Bind(RequiredSettings settings, IConfiguration configuration)
    {
        settings.LabConnectionString = configuration.GetConnectionString("Lab");
        settings.LabsBaseAddress = configuration["Labs:BaseAddress"];
        settings.SmsBaseAddress = configuration["Sms:BaseAddress"];
        settings.AuthAuthority = configuration["Auth:Authority"];
    }

    /// <summary>Whether every required setting is present and well-formed.</summary>
    /// <returns><c>true</c> when valid.</returns>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(LabConnectionString)
        && IsAbsolute(LabsBaseAddress)
        && IsAbsolute(SmsBaseAddress)
        && IsAbsolute(AuthAuthority);

    private static bool IsAbsolute(string? value) => Uri.TryCreate(value, UriKind.Absolute, out _);
}
