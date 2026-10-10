using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Localization;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.EfCore;

public sealed class MutableCaller : ICaller
{
    public int Id { get; set; }
}

/// <summary>The same checks on both engines.</summary>
public abstract class EfCoreTests<TFixture>(TFixture pg) where TFixture : VisitsFixture
{
    private async Task SeedAsync(int patientId, string notes)
    {
        await using var db = pg.NewContext(patientId);
        db.Visits.Add(new Visit(patientId, notes));
        await db.SaveChangesAsync();
    }

    private ServiceProvider Services(TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(time ?? TimeProvider.System);
        services.AddScoped<MutableCaller>();
        services.AddScoped<ICaller>(sp => sp.GetRequiredService<MutableCaller>());
        services.AddVisitsDb(options => pg.UseDatabase(options, retry: true));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<T> InScopeAsync<T>(ServiceProvider sp, int callerId, Func<VisitsDbContext, Task<T>> work)
    {
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MutableCaller>().Id = callerId;
        return await work(scope.ServiceProvider.GetRequiredService<VisitsDbContext>());
    }

    [Fact]
    public async Task OwnerFilter_EachCallerSeesOnlyTheirOwnVisits()
    {
        await SeedAsync(101, "visit of 101");
        await SeedAsync(102, "visit of 102");
        await using var sp = Services();

        var seenBy101 = await InScopeAsync(sp, 101, db => db.Visits.Select(v => v.Notes).ToListAsync());
        var seenBy102 = await InScopeAsync(sp, 102, db => db.Visits.Select(v => v.Notes).ToListAsync());

        Assert.Equal("visit of 101", Assert.Single(seenBy101));
        Assert.Equal("visit of 102", Assert.Single(seenBy102));
    }

    [Fact]
    public async Task CallerCopiedIntoALocal_EveryContextGetsTheFirstCallersRows()
    {
        await SeedAsync(201, "visit of 201");
        await SeedAsync(202, "visit of 202");

        await using (var first = pg.NewLeakyContext(201))
        {
            Assert.Equal("visit of 201", Assert.Single(await first.Visits.Select(v => v.Notes).ToListAsync()));
        }

        await using var second = pg.NewLeakyContext(202);
        var seenBy202 = await second.Visits.Select(v => v.Notes).ToListAsync();
        Assert.Equal("visit of 201", Assert.Single(seenBy202));   // patient 202 is shown patient 201's visit
    }

    [Fact]
    public async Task IgnoringOnlyTheOwnerFilter_StillHidesDeletedVisits()
    {
        await SeedAsync(301, "kept");
        await SeedAsync(302, "deleted");
        await using (var owner = pg.NewContext(302))
        {
            (await owner.Visits.SingleAsync()).Delete();
            await owner.SaveChangesAsync();
        }

        await using var admin = pg.NewContext(callerId: 0);
        var notes = await admin.Visits
            .IgnoreQueryFilters([VisitsDbContext.OwnerFilter])
            .Where(v => v.PatientId == 301 || v.PatientId == 302)
            .Select(v => v.Notes)
            .ToListAsync();

        Assert.Equal("kept", Assert.Single(notes));
    }

    [Fact]
    public async Task PooledContext_AuditStampsTheCallerOfEachRequest()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        await using var sp = Services(time);

        foreach (var caller in new[] { 401, 402 })
        {
            await InScopeAsync(sp, caller, async db =>
            {
                db.Visits.Add(new Visit(caller, "new"));
                return await db.SaveChangesAsync();
            });
        }

        await using var check = pg.NewContext(callerId: 0);
        var rows = await check.Visits.IgnoreQueryFilters()
            .Where(v => v.PatientId == 401 || v.PatientId == 402)
            .OrderBy(v => v.PatientId)
            .ToListAsync();

        Assert.Collection(rows, r => Assert.Equal(401, r.CreatedBy), r => Assert.Equal(402, r.CreatedBy));
        Assert.All(rows, r => Assert.Equal(time.GetUtcNow(), r.CreatedAt));
    }

    [Fact]
    public async Task PooledContext_RentedFromTheFactoryDirectly_KeepsThePreviousRequestsCaller()
    {
        await using var sp = Services();
        await InScopeAsync(sp, 451, db => db.Visits.CountAsync());

        await using var rented = sp.GetRequiredService<IDbContextFactory<VisitsDbContext>>().CreateDbContext();

        Assert.Equal(451, rented.CallerId);
    }

    [Fact]
    public async Task ExecuteUpdate_KeepsTheOwnerFilter_ButSkipsTheAuditInterceptor()
    {
        await SeedAsync(501, "before");
        await SeedAsync(502, "someone else's");

        await using (var db = pg.NewContext(501))
        {
            var changed = await db.Visits.ExecuteUpdateAsync(s => s.SetProperty(v => v.Notes, "bulk"));
            Assert.Equal(1, changed);
        }

        await using var check = pg.NewContext(callerId: 0);
        var rows = await check.Visits.IgnoreQueryFilters()
            .Where(v => v.PatientId == 501 || v.PatientId == 502)
            .OrderBy(v => v.PatientId)
            .ToListAsync();
        Assert.Collection(
            rows,
            r => { Assert.Equal("bulk", r.Notes); Assert.Null(r.UpdatedBy); },
            r => Assert.Equal("someone else's", r.Notes));
    }

    [Fact]
    public async Task SaveWithTheVersionTheClientLoaded_AfterSomeoneElseSaved_GetsAConcurrencyException()
    {
        await SeedAsync(601, "v1");
        string tokenAdminBLoaded;
        await using (var formB = pg.NewContext(601))
        {
            tokenAdminBLoaded = VersionToken.Read(formB, await formB.Visits.SingleAsync());
        }

        await using (var adminA = pg.NewContext(601))
        {
            (await adminA.Visits.SingleAsync()).Amend("from a");
            await adminA.SaveChangesAsync();
        }

        await using var adminB = pg.NewContext(601);
        var visit = await adminB.Visits.SingleAsync();   // loads A's version
        VersionToken.Expect(adminB, visit, tokenAdminBLoaded);
        visit.Amend("from b");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => adminB.SaveChangesAsync());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientEditsTheSameNoteOnTwoDevices_TheSecondSaveIsRefused_AndTheRetryWins()
    {
        // Given: a visit note, opened on the phone and on the tablet at the same moment
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        await SeedAsync(811, "headache");
        string phoneToken, tabletToken;
        await using (var phone = pg.NewContext(811, time))
        await using (var tablet = pg.NewContext(811, time))
        {
            phoneToken = VersionToken.Read(phone, await phone.Visits.SingleAsync());
            tabletToken = VersionToken.Read(tablet, await tablet.Visits.SingleAsync());
        }

        // When: the phone saves first, then the tablet saves with the version it opened
        time.Advance(TimeSpan.FromMinutes(1));
        await SaveAsync(phoneToken, "headache since Monday");
        time.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => SaveAsync(tabletToken, "migraine"));

        // ...and the tablet reloads, sees the phone's text, and saves on top of it
        string reloaded;
        await using (var tablet = pg.NewContext(811, time))
        {
            var visit = await tablet.Visits.SingleAsync();
            reloaded = visit.Notes;
            visit.Amend(visit.Notes + "; migraine");
            await tablet.SaveChangesAsync();
        }

        // Then: nothing was lost, and the audit names the last edit
        await using var check = pg.NewContext(811);
        var saved = await check.Visits.SingleAsync();
        Assert.Equal("headache since Monday", reloaded);
        Assert.Equal("headache since Monday; migraine", saved.Notes);
        Assert.Equal((811, time.GetUtcNow()), (saved.UpdatedBy!.Value, saved.UpdatedAt!.Value));

        async Task SaveAsync(string token, string notes)
        {
            await using var device = pg.NewContext(811, time);
            var visit = await device.Visits.SingleAsync();
            VersionToken.Expect(device, visit, token);
            visit.Amend(notes);
            await device.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ColumnTypes_FollowTheDbaRulesOnThisEngine()
    {
        await using var db = pg.NewContext(callerId: 0);
        var visit = db.Model.FindEntityType(typeof(Visit))!;
        string ColumnType(string name) => visit.FindProperty(name)!.GetColumnType();

        if (pg.Engine == Engine.PostgreSql)
        {
            Assert.Equal("character varying(500)", ColumnType(nameof(Visit.Notes)));
            Assert.Equal("timestamptz", ColumnType(nameof(Visit.CreatedAt)));
            Assert.Equal("xid", ColumnType(VisitConfiguration.Version));
        }
        else
        {
            Assert.Equal("nvarchar(500)", ColumnType(nameof(Visit.Notes)));
            Assert.Equal("datetime2(3)", ColumnType(nameof(Visit.CreatedAt)));
            Assert.Equal("rowversion", ColumnType(VisitConfiguration.Version));
        }
    }

    [Fact]
    public async Task RetryingStrategy_OwnTransactionThrows_InsideTheStrategyCommits()
    {
        await using var db = pg.NewContext(701, retry: true);
        db.Visits.Add(new Visit(701, "outside"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
        });

        var saved = await db.InTransactionAsync(
            async ct =>
            {
                db.Visits.Add(new Visit(701, "inside"));
                return await db.SaveChangesAsync(ct);
            },
            CancellationToken.None);

        Assert.Equal(1, saved);
        Assert.Equal("inside", Assert.Single(await db.Visits.Select(v => v.Notes).ToListAsync()));
    }

    [Fact]
    public async Task TwoSavesInOneUnit_TransientFailureOnTheSecond_RetryCommitsBothOnce()
    {
        var failSecondInsertOnce = new FailOnceInterceptor(failOnInsert: 2, pg.TransientError);
        await using var db = pg.NewContext(801, retry: true, extra: failSecondInsertOnce);

        await db.InTransactionAsync(
            async ct =>
            {
                db.Visits.Add(new Visit(801, "first"));
                await db.SaveChangesAsync(ct);
                db.Visits.Add(new Visit(801, "second"));
                return await db.SaveChangesAsync(ct);
            },
            CancellationToken.None);

        Assert.True(failSecondInsertOnce.Failed);
        var notes = await db.Visits.OrderBy(v => v.Id).Select(v => v.Notes).ToListAsync();
        Assert.Collection(notes, n => Assert.Equal("first", n), n => Assert.Equal("second", n));
    }
}

public sealed class PostgresEfCoreTests(PostgresVisits pg) : EfCoreTests<PostgresVisits>(pg), IClassFixture<PostgresVisits>;

public sealed class SqlServerEfCoreTests(SqlServerVisits db) : EfCoreTests<SqlServerVisits>(db), IClassFixture<SqlServerVisits>;

/// <summary>Throws a transient error on the Nth INSERT, once, as a dropped connection would.</summary>
public sealed class FailOnceInterceptor(int failOnInsert, Func<Exception> transientError) : DbCommandInterceptor
{
    private int _inserts;

    public bool Failed { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (!Failed && command.CommandText.Contains("INSERT", StringComparison.Ordinal) && ++_inserts == failOnInsert)
        {
            Failed = true;
            throw transientError();
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// The slim image's conditions: no ICU, no tzdata, UTC. PostgreSQL only: SqlClient refuses to connect
/// without ICU (see AuthServer/SlimImageTests).
/// </summary>
public sealed class PostgresEfCoreProductionTests(PostgresVisits pg) : IClassFixture<PostgresVisits>
{
    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ARiyadhDayIsQueriedAsAUtcRange()
    {
        ProductionConditions.Require();

        // Given: a visit at 23:30 and one at 00:30 the next day, Riyadh time, on a pod that runs in UTC
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 20, 30, 0, TimeSpan.Zero));
        await using (var db = pg.NewContext(821, time))
        {
            db.Visits.Add(new Visit(821, "late evening"));
            await db.SaveChangesAsync();
            time.Advance(TimeSpan.FromHours(1));
            db.Visits.Add(new Visit(821, "after midnight"));
            await db.SaveChangesAsync();
        }

        // When: the clinic asks for 10 October, its own day. Npgsql only takes a UTC DateTimeOffset for
        // timestamptz, so the local day becomes a UTC range before the query.
        var offset = RiyadhTime.Zone.GetUtcOffset(new DateTime(2026, 10, 10));
        var from = new DateTimeOffset(2026, 10, 10, 0, 0, 0, offset).ToUniversalTime();
        var to = from.AddDays(1);
        await using var clinic = pg.NewContext(821);
        var visits = await clinic.Visits.Where(v => v.CreatedAt >= from && v.CreatedAt < to).ToListAsync();

        // Then: only the 23:30 visit, stored in UTC and shown back in Riyadh time
        var visit = Assert.Single(visits);
        Assert.Equal("late evening", visit.Notes);
        Assert.Equal(TimeSpan.Zero, visit.CreatedAt.Offset);
        Assert.Equal(new TimeOnly(23, 30), TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(visit.CreatedAt, RiyadhTime.Zone).DateTime));
    }
}
