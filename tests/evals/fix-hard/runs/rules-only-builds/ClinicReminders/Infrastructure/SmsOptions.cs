namespace ClinicReminders.Infrastructure;

public sealed class SmsOptions
{
    public const string Section = "Sms";

    /// <summary>The provider's API root. Relative paths resolve under it, so it is used with a trailing slash.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>How long one send may take. Kept short: a hung call holds the job, and its outcome is unknown anyway.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public Uri BaseAddress => new(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

    public bool IsValid() =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && Timeout > TimeSpan.Zero && Timeout <= TimeSpan.FromMinutes(1);
}
