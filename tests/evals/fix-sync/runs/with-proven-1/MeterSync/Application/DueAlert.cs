using MeterSync.Domain;

namespace MeterSync.Application;

public sealed record DueAlert(Reading Reading, string? Phone);
