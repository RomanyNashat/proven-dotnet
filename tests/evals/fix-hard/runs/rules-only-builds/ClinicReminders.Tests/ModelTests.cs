using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Tests;

/// <summary>The schema the model produces, read without a database (EF generates the script offline).</summary>
public sealed class ModelTests
{
    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<RemindersDbContext>().UseSqlServer("Server=unused").Options;
        using var db = new RemindersDbContext(options);
        return db.Database.GenerateCreateScript();
    }

    [Fact]
    public void Schema_HasNoUnboundedOrBinaryColumns()
    {
        var script = CreateScript();

        script.Should().NotContainAny("(max)", "varbinary", " image");
    }

    [Fact]
    public void Schema_IndexesThePagingAndSendQueries()
    {
        var script = CreateScript();

        script.Should().Contain("[IX_Reminders_PatientId_CreatedAt_Id]").And.Contain("[IX_Reminders_Status_AppointmentAt]");
    }
}
