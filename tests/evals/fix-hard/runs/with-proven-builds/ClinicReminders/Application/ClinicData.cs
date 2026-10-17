using Microsoft.Extensions.Compliance.Classification;

namespace ClinicReminders.Application;

/// <summary>The service's data classes. A redactor is chosen per class, at startup.</summary>
public static class ClinicData
{
    public const string Taxonomy = "Clinic";

    /// <summary>Identifies a patient on its own: mobile, national ID, MRN.</summary>
    public static DataClassification PatientIdentifier => new(Taxonomy, nameof(PatientIdentifier));
}
