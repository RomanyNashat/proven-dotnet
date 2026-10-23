using System.ComponentModel.DataAnnotations;

namespace MeterSync.Infrastructure;

/// <summary>Where a downstream service lives. Bound from "MeterVendor" and "Sms".</summary>
public sealed class ServiceAddressOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }
}
