using Microsoft.Extensions.Compliance.Classification;

namespace ClinicReminders.Application;

/// <summary>Marks a log parameter or a property as <see cref="ClinicData.PatientIdentifier"/>.</summary>
public sealed class PatientIdentifierAttribute() : DataClassificationAttribute(ClinicData.PatientIdentifier);
