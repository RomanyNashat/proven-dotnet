namespace MeterSync.Infrastructure;

/// <summary>Where an external HTTP service lives (<c>MeterVendor</c> and <c>Sms</c> sections).</summary>
public sealed class HttpServiceOptions
{
    public const string MeterVendor = "MeterVendor";
    public const string Sms = "Sms";

    public Uri? BaseAddress { get; set; }

    public bool IsValid => BaseAddress is { IsAbsoluteUri: true };
}
