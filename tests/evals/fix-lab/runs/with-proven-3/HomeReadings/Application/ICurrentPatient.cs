namespace HomeReadings.Application;

/// <summary>The signed-in patient.</summary>
public interface ICurrentPatient
{
    /// <summary>The patient's id: the token's <c>sub</c>.</summary>
    int Id { get; }
}
