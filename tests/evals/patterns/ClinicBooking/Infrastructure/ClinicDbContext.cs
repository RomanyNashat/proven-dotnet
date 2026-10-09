using ClinicBooking.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicBooking.Infrastructure;

public sealed class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
}

public sealed class Clinic
{
    public int Id { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public required string City { get; init; }
}
