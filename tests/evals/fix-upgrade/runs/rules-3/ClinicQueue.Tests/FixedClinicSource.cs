using ClinicQueue.Application;

namespace ClinicQueue.Tests;

public sealed class FixedClinicSource : IClinicSource
{
    public static readonly IReadOnlyList<ClinicEntry> Clinics = [new(1, "North"), new(2, "South")];

    public IReadOnlyList<ClinicEntry> Load() => Clinics;
}
