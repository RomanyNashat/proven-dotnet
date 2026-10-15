namespace Clinic.Tests;

public sealed class DatabaseFixture
{
    public Task InitializeAsync(ClinicDbContext db) => db.Database.EnsureCreatedAsync();
}
