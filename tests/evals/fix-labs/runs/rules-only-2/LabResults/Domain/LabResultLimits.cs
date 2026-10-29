namespace LabResults.Domain;

/// <summary>Column sizes, shared by the table mapping and request validation so the two can't drift.</summary>
public static class LabResultLimits
{
    public const int Test = 40;
    public const int Unit = 16;
    public const int Notes = 1000;
    public const int RecordedBy = 64;
}
