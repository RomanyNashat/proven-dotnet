using Microsoft.EntityFrameworkCore;

namespace Appointments;

public sealed class Appointment(int patientId, int doctorId, DateTimeOffset at)
{
    public int Id { get; private set; }

    public int PatientId { get; private set; } = patientId;

    public int DoctorId { get; private set; } = doctorId;

    public DateTimeOffset At { get; private set; } = at;

    public string? PaymentId { get; private set; }

    public void MarkPaid(string paymentId) => PaymentId ??= paymentId;
}

public sealed class Clinic
{
    public int Id { get; private set; }

    public decimal Price { get; private set; }
}

public sealed class AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public async Task MarkPaidAsync(int appointmentId, string paymentId, CancellationToken ct)
    {
        var appointment = await Appointments.FindAsync([appointmentId], ct);
        appointment?.MarkPaid(paymentId);
        await SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Appointment>(e =>
        {
            e.Property(a => a.At).HasColumnType("timestamptz");
            e.Property(a => a.PaymentId).HasMaxLength(64);
        });
        model.Entity<Clinic>(e => e.Property(c => c.Price).HasColumnType("numeric(10,2)"));
    }
}
