using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SkillSamples.Cqrs;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Compliance;

public sealed record ExportForResearch(int SubjectId) : ICommand<int>, IRequiresConsent
{
    public string Purpose => "research";
}

public sealed class CountingExport : ICommandHandler<ExportForResearch, int>
{
    public int Calls;

    public Task<int> HandleAsync(ExportForResearch command, CancellationToken ct) => Task.FromResult(Interlocked.Increment(ref Calls));
}

public sealed class ComplianceTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);

    private sealed class User(string id) : ICurrentUser
    {
        public string Id => id;
    }

    private ComplianceDbContext Context(string userId = "dr-a", string? connectionString = null) =>
        new(new DbContextOptionsBuilder<ComplianceDbContext>()
            .UseNpgsql(connectionString ?? db.ConnectionString)
            .AddInterceptors(new PhiAuditInterceptor(new User(userId), _time))
            .Options);

    // Stands in for the reviewed migration script the pipeline applies.
    public async Task InitializeAsync()
    {
        if (await db.ScalarAsync<bool>("SELECT to_regclass('phi_audit') IS NOT NULL"))
            return;
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<(string Action, string UserId, string Columns, DateTimeOffset At)>> AuditOf(int diagnosisId)
    {
        await using var context = Context();
        var rows = await context.PhiAudit
            .Where(a => a.RecordType == nameof(Diagnosis) && a.RecordId == diagnosisId)
            .OrderBy(a => a.Id)
            .ToListAsync();
        return rows.Select(r => (r.Action, r.UserId, r.ChangedColumns, r.At)).ToList();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ADoctorRevisesADiagnosis_TheAuditSaysWhoWhenAndWhichColumnButNotTheValue()
    {
        // Given: one doctor records a diagnosis
        int id;
        await using (var context = Context("dr-a"))
        {
            var diagnosis = new Diagnosis(patientId: 5, "E11.9");
            await context.Diagnoses.AddAsync(diagnosis);   // HiLo may fetch the next block: AddAsync
            await context.SaveChangesAsync();
            id = diagnosis.Id;
        }

        // When: another revises it an hour later, by setting the property (no Update call)
        _time.Advance(TimeSpan.FromHours(1));
        await using (var context = Context("dr-b"))
        {
            (await context.Diagnoses.SingleAsync(d => d.Id == id)).Revise("E11.65");
            await context.SaveChangesAsync();
        }

        // Then: two audit rows with the real id, who and when, the column name and no diagnosis code
        var audit = await AuditOf(id);
        Assert.Equal(
            new[] { ("Added", "dr-a", "", Now), ("Modified", "dr-b", "Code", Now.AddHours(1)) },
            audit.ToArray());
        Assert.DoesNotContain(audit, a => a.Columns.Contains("E11"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheSaveFails_NoAuditRowClaimsTheChangeHappened()
    {
        await using var context = Context();
        var diagnosis = new Diagnosis(patientId: 6, "NOT-A-REAL-CODE");   // longer than the column allows
        await context.Diagnoses.AddAsync(diagnosis);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.NotEqual(0, diagnosis.Id);
        Assert.Empty(await AuditOf(diagnosis.Id));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_SomeoneTriesToTidyTheAuditLog_TheDatabaseRefuses()
    {
        // Given: the service connects as its own role, with the grants from the reviewed script
        var role = $"app_{Guid.NewGuid():N}";
        await db.ExecuteAsync($"CREATE ROLE {role} LOGIN PASSWORD 'samples'");
        var grants = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Compliance", "audit_grants.sql"));
        await db.ExecuteAsync(grants.Replace("clinic_app", role, StringComparison.Ordinal));
        var asService = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Username = role, Password = "samples" }.ConnectionString;
        try
        {
            // When: it does its normal work, then tries to change or remove the audit
            int id;
            await using (var context = Context("dr-a", asService))
            {
                var diagnosis = new Diagnosis(patientId: 7, "J45.909");
                await context.Diagnoses.AddAsync(diagnosis);
                await context.SaveChangesAsync();
                id = diagnosis.Id;
            }

            await using var connection = new NpgsqlConnection(asService);
            await connection.OpenAsync();
            async Task<string?> Refusal(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("id", id);
                try { await command.ExecuteNonQueryAsync(); return null; }
                catch (PostgresException ex) { return ex.SqlState; }
            }

            // Then: the work succeeded and its audit row is there; changing or removing it is refused
            Assert.Single(await AuditOf(id));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Refusal("UPDATE phi_audit SET user_id = 'someone-else' WHERE record_id = @id"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Refusal("DELETE FROM phi_audit WHERE record_id = @id"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await db.ExecuteAsync($"DROP OWNED BY {role}; DROP ROLE {role}");
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientWithdrawsResearchConsent_TheirDataIsNoLongerExported()
    {
        // Given: patient 77 agreed to research use
        await using var context = Context();
        var consent = Consent.Grant(subjectId: 77, "research", _time);
        context.Consents.Add(consent);
        await context.SaveChangesAsync();
        var export = new CountingExport();
        var handler = new ConsentDecorator<ExportForResearch, int>(export, new ConsentStore(context));

        Assert.Equal(1, await handler.HandleAsync(new ExportForResearch(77), CancellationToken.None));

        // When: they withdraw it
        consent.Withdraw(_time);
        await context.SaveChangesAsync();

        // Then: the next export for them is refused, as is one for a patient who never agreed
        await Assert.ThrowsAsync<ConsentRequiredException>(() => handler.HandleAsync(new ExportForResearch(77), CancellationToken.None));
        await Assert.ThrowsAsync<ConsentRequiredException>(() => handler.HandleAsync(new ExportForResearch(78), CancellationToken.None));
        Assert.Equal(1, export.Calls);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_ADoctorBreaksTheGlassAtNight_OnePatientForThirtyMinutes()
    {
        var night = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => EmergencyAccessGrant.Open("dr-c", 5, "urgent", night));

        var grant = EmergencyAccessGrant.Open("dr-c", 5, "Unconscious patient in the ER, no records on file", night);

        Assert.True(grant.Allows(5, night));
        Assert.False(grant.Allows(6, night));
        night.Advance(EmergencyAccessGrant.Lasts);
        Assert.False(grant.Allows(5, night));
    }

    [Theory]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    [InlineData("sa-central-1", true)]
    [InlineData("eu-west-1", false)]
    public async Task Story_StorageIsPointedAtAnotherRegion_TheServiceRefusesToStart(string region, bool starts)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataResidency:StorageRegion"] = region,
            ["DataResidency:AllowedRegions:0"] = "sa-central-1",
            ["DataResidency:AllowedRegions:1"] = "sa-west-1"
        });
        builder.Services.AddDataResidencyCheck();
        using var host = builder.Build();

        var start = async () => { await host.StartAsync(); await host.StopAsync(); };

        if (starts)
            await start();
        else
            Assert.Contains("StorageRegion", (await Assert.ThrowsAsync<OptionsValidationException>(start)).Message);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheAuditIsWrittenAndReadBackInUtc()
    {
        ProductionConditions.Require();

        int id;
        await using (var context = Context("dr-d"))
        {
            var diagnosis = new Diagnosis(patientId: 8, "I10");
            await context.Diagnoses.AddAsync(diagnosis);
            await context.SaveChangesAsync();
            id = diagnosis.Id;
        }

        var row = Assert.Single(await AuditOf(id));
        Assert.Equal(("Added", "dr-d", Now), (row.Action, row.UserId, row.At));
        Assert.Equal(TimeSpan.Zero, row.At.Offset);
        Assert.Equal("2026-10-11 09:00:00", await db.ScalarAsync<string>(
            "SELECT to_char(at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') FROM phi_audit WHERE record_id = @id", new { id }));
    }
}
