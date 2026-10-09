using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkillSamples.Localization;

/// <summary>The same checks on both engines.</summary>
public abstract class BilingualDataTests<TFixture>(TFixture fixture) where TFixture : HospitalsFixture
{
    protected TFixture Db => fixture;

    private async Task<int> AddAsync(string nameAr, string nameEn)
    {
        await using var db = fixture.NewContext();
        var hospital = new Hospital(nameAr, nameEn);
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        return hospital.Id;
    }

    [Fact]
    public async Task ArabicName_ComesBackExactly()
    {
        var id = await AddAsync("مستشفى المَدينة", "Al Madinah Hospital");

        await using var db = fixture.NewContext();
        var stored = await db.Hospitals.AsNoTracking().SingleAsync(h => h.Id == id);

        Assert.Equal("مستشفى المَدينة", stored.NameAr);
        Assert.Equal("مستشفي المدينه", stored.NameArSearch);
    }

    [Fact]
    public async Task Search_TypedWithoutHamzaOrTashkeel_FindsTheName()
    {
        var id = await AddAsync("مستشفى أحمد التخصصي", "Ahmed Specialist Hospital");

        await using var db = fixture.NewContext();
        var found = await db.SearchAsync(LanguageHeader.Arabic, "مستشفي احمد", CancellationToken.None);

        Assert.Equal(new HospitalItem(id, "مستشفى أحمد التخصصي"), Assert.Single(found));
    }

    [Fact]
    public async Task Search_EnglishRequest_ReturnsOnlyTheEnglishName()
    {
        var id = await AddAsync("مركز الشفاء الطبي", "Al Shifa Medical Center");

        await using var db = fixture.NewContext();
        var found = await db.SearchAsync(LanguageHeader.English, "مركز الشفاء", CancellationToken.None);

        Assert.Equal(new HospitalItem(id, "Al Shifa Medical Center"), Assert.Single(found));
    }

    [Fact]
    public async Task NameColumns_AreBoundedAndUnicodeOnThisEngine()
    {
        await using var db = fixture.NewContext();
        var hospital = db.Model.FindEntityType(typeof(Hospital))!;

        var expected = fixture.SqlServer ? "nvarchar(200)" : "character varying(200)";
        Assert.All(
            new[] { nameof(Hospital.NameAr), nameof(Hospital.NameEn), nameof(Hospital.NameArSearch) },
            name => Assert.Equal(expected, hospital.FindProperty(name)!.GetColumnType()));
    }

    [Fact]
    public async Task PlainVarcharColumn_ArabicSurvivesOnlyOnPostgreSql()
    {
        await using var db = fixture.NewContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE plain_varchar (v varchar(50) NOT NULL)");
        await db.Database.ExecuteSqlAsync($"INSERT INTO plain_varchar (v) VALUES ({"مستشفى"})");

        var stored = await db.Database.SqlQueryRaw<string>("SELECT v AS \"Value\" FROM plain_varchar").SingleAsync();

        // SQL Server's default collation has a Latin code page: each Arabic letter becomes '?', silently.
        Assert.Equal(fixture.SqlServer ? "??????" : "مستشفى", stored);
    }
}

public sealed class PostgresBilingualDataTests(PostgresHospitals db)
    : BilingualDataTests<PostgresHospitals>(db), IClassFixture<PostgresHospitals>;

public sealed class SqlServerBilingualDataTests(SqlServerHospitals db)
    : BilingualDataTests<SqlServerHospitals>(db), IClassFixture<SqlServerHospitals>;
