using MeterSync.Domain;

namespace MeterSync.Application;

public sealed record LinkResult(LinkOutcome Outcome, Meter? Meter = null);
