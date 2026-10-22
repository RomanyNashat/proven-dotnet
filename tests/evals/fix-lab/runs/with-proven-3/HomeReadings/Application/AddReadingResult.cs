using HomeReadings.Domain;

namespace HomeReadings.Application;

/// <summary>The outcome of adding a reading: the reading, or the errors that stopped it.</summary>
public sealed record AddReadingResult(Reading? Reading, IReadOnlyDictionary<string, string[]> Errors);
