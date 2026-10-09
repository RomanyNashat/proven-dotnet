using ClinicBooking.Domain;
using ClinicBooking.Infrastructure;

namespace ClinicBooking.Application;

public sealed class CheckInHandler(IRepository<Appointment> appointments, ClinicDbContext db)
{
    public async Task<bool> HandleAsync(int id, CancellationToken ct)
    {
        var a = appointments.GetById(id);
        if (a is null || a.IsCancelled || a.IsCompleted) return false;
        a.IsCheckedIn = true;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CancelHandler(IRepository<Appointment> appointments, ClinicDbContext db)
{
    public async Task<bool> HandleAsync(int id, CancellationToken ct)
    {
        var a = appointments.GetById(id);
        if (a is null || a.IsCheckedIn || a.IsCompleted) return false;
        a.IsCancelled = true;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CompleteHandler(IRepository<Appointment> appointments, ClinicDbContext db)
{
    public async Task<bool> HandleAsync(int id, CancellationToken ct)
    {
        var a = appointments.GetById(id);
        if (a is null || !a.IsCheckedIn || a.IsCancelled) return false;
        a.IsCompleted = true;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class TodayListHandler(IRepository<Appointment> appointments, TimeProvider time)
{
    public IReadOnlyList<Appointment> Handle(int clinicId)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return appointments.GetAll().Where(a => a.ClinicId == clinicId && a.Day == today).ToList();
    }
}
