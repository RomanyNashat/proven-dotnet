using Microsoft.Extensions.Compliance.Classification;

namespace SkillSamples.PiiMasking;

/// <summary>The service's data classes. A redactor is chosen per class, at startup.</summary>
public static class ClinicData
{
    public const string Taxonomy = "Clinic";

    /// <summary>Identifies a patient on its own: national ID, mobile, MRN.</summary>
    public static DataClassification PatientIdentifier => new(Taxonomy, nameof(PatientIdentifier));
}

/// <summary>Marks a log parameter or a property as <see cref="ClinicData.PatientIdentifier"/>.</summary>
public sealed class PatientIdentifierAttribute() : DataClassificationAttribute(ClinicData.PatientIdentifier);
