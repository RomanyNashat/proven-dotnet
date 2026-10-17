using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Tests.Infrastructure;

public sealed class SchemaScriptTests
{
    [Fact]
    public void CreateScript_MatchesTheModel()
    {
        var options = new DbContextOptionsBuilder<RemindersDbContext>().UseSqlServer("Server=none;Database=Reminders").Options;
        using var db = new RemindersDbContext(options);

        var script = File.ReadAllText(ScriptPath("001_create_schema.up.sql"));

        Normalize(script).Should().Contain(Normalize(db.Database.GenerateCreateScript()));
    }

    [Fact]
    public void CreateScript_HasNoUnboundedOrOffsetColumns()
    {
        var script = File.ReadAllText(ScriptPath("001_create_schema.up.sql"));

        script.Should().NotContainAny("(max)", "datetimeoffset", "varbinary", " text");
    }

    private static string ScriptPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "ClinicReminders", "db")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "ClinicReminders", "db", name);
    }

    private static string Normalize(string sql) =>
        string.Join('\n', sql.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));
}
