using ClinicQueue.Infrastructure;

namespace ClinicQueue.Application;

public sealed class EfClinicSource(QueueDbContext db) : IClinicSource
{
    public IReadOnlyList<ClinicEntry> Load() =>
        db.Clinics.OrderBy(c => c.Id).Select(c => new ClinicEntry(c.Id, c.Name)).ToList();
}
