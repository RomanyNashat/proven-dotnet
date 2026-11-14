namespace ClinicQueue.Application;

public sealed record ClinicEntry(int Id, string Name);

/// <summary>Where the clinic list comes from: the database, in production.</summary>
public interface IClinicSource
{
    IReadOnlyList<ClinicEntry> Load();
}

/// <summary>The clinic list every request reads, kept in memory and refreshed by <see cref="DirectoryWarmer"/>.</summary>
public sealed class ClinicDirectory
{
    private volatile IReadOnlyList<ClinicEntry> _all = [];

    public IReadOnlyList<ClinicEntry> All => _all;

    public void Replace(IReadOnlyList<ClinicEntry> clinics) => _all = clinics;
}
