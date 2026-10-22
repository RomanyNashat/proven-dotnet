namespace HomeReadings.Application;

/// <summary>The patient making the request: the token's <c>sub</c>.</summary>
public interface ICurrentPatient
{
    int Id { get; }
}
