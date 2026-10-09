using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkillSamples.Localization;

public static class AppointmentReference
{
    public static string BuildNaive(DateTime date, int number) => $"APT-{date:yyyyMMdd}-{number}";

    public static string Build(DateTime date, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"APT-{date:yyyyMMdd}-{number}");
}

public sealed class InMemorySource(Dictionary<string, Dictionary<string, string>> languages) : ITranslationSource
{
    public int Loads;

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Loads);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(languages.GetValueOrDefault(language) ?? []);
    }
}

public sealed class TempEnvironment(string root) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "SkillSamples";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

[Collection("culture")]   // these tests change the thread culture
public sealed class LocalizationTests
{
    private static readonly DateTime Oct4 = new(2026, 10, 4);

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData(" AR ", "ar")]
    [InlineData("en", "en")]
    [InlineData("fr", "en")]
    [InlineData(null, "en")]
    public void Header_ParsesToArOrEn(string? header, string expected) => Assert.Equal(expected, LanguageHeader.Parse(header));

    [Fact]
    public void Trap_ArSaIsHijri_AndCantParseAGregorianDate()
    {
        var arSa = CultureInfo.GetCultureInfo("ar-SA");

        Assert.IsType<UmAlQuraCalendar>(arSa.Calendar);
        Assert.Throws<FormatException>(() => DateTime.Parse("2026-10-04", arSa));
        Assert.NotEqual("1,234.50", 1234.5.ToString("N2", arSa));
    }

    [Theory]
    [InlineData("ar", "en-US", "ar-SA")]
    [InlineData("en", "en-US", "en-US")]
    [InlineData(null, "en-US", "en-US")]
    public async Task Middleware_HeaderSetsTextCultureOnly(string? header, string culture, string uiCulture)
    {
        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(culture: "en-US", uiCulture: "en-US"),
            SupportedCultures = [new CultureInfo("en-US")],
            SupportedUICultures = [new CultureInfo("en-US"), new CultureInfo("ar-SA")],
            RequestCultureProviders = [new XLanguageCultureProvider()],
        };
        string? seenCulture = null, seenUi = null;
        var middleware = new RequestLocalizationMiddleware(
            _ =>
            {
                seenCulture = CultureInfo.CurrentCulture.Name;
                seenUi = CultureInfo.CurrentUICulture.Name;
                return Task.CompletedTask;
            },
            Options.Create(options), NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers[LanguageHeader.Name] = header;
        }

        using (new CultureScope("en-US"))
        {
            await middleware.Invoke(context);
        }

        Assert.Equal(culture, seenCulture);
        Assert.Equal(uiCulture, seenUi);
    }

    [Fact]
    public void Guard_PassesWhenIcuIsPresent() => GlobalizationGuard.RequireArabicCulture();

    [Fact]
    public async Task Catalog_FallsBackArToEnToKey_AndLoadsEachLanguageOnce()
    {
        var source = new InMemorySource(new()
        {
            ["en"] = new() { ["Errors.NotFound"] = "Not found", ["Only.En"] = "English only" },
            ["ar"] = new() { ["Errors.NotFound"] = "غير موجود" },
        });
        var catalog = new TranslationCatalog(source);

        Assert.Equal("غير موجود", await catalog.GetAsync("Errors.NotFound", "ar"));
        Assert.Equal("English only", await catalog.GetAsync("Only.En", "ar"));
        Assert.Equal("Missing.Key", await catalog.GetAsync("Missing.Key", "ar"));
        Assert.Equal("Not found", await catalog.GetAsync("Errors.NotFound", "en"));
        Assert.Equal(2, source.Loads);
    }

    [Fact]
    public async Task FileSource_ReadsI18nFolder()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "i18n"));
        await File.WriteAllTextAsync(Path.Combine(root, "i18n", "ar.json"), """{ "Errors.AppointmentNotFound": "الموعد {0} غير موجود" }""");

        var texts = await new FileTranslationSource(new TempEnvironment(root)).LoadAsync("ar", default);

        Assert.Equal("الموعد 7 غير موجود", string.Format(CultureInfo.InvariantCulture, texts["Errors.AppointmentNotFound"], 7));
    }

    [Fact]
    public void Dates_ArabicGregorianAndHijri_AreBothExplicit()
    {
        Assert.Equal("4 أكتوبر 2026", Oct4.ToString("d MMMM yyyy", Cultures.ArabicGregorian));
        Assert.Equal("23 ربيع الآخر 1448", Oct4.ToString("d MMMM yyyy", Cultures.ArabicHijri));
    }

    [Fact]
    public void RiyadhTime_IsUtcPlusThree()
    {
        var utc = new DateTime(2026, 10, 4, 21, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 5, 0, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(utc, RiyadhTime.Zone));
    }

    [Theory]
    [InlineData("أَحْمَد", "احمد")]
    [InlineData("مستشفى المَدينة", "مستشفي المدينه")]
    [InlineData("إسـلام", "اسلام")]
    public void Search_NormalizesSpellingVariants(string input, string expected) =>
        Assert.Equal(expected, ArabicSearch.NormalizeForSearch(input));

    [Fact]
    public void Reference_UnderArabicCulture_InvariantStaysGregorian_NaiveTurnsHijri()
    {
        using var _ = new CultureScope("ar-SA");

        Assert.Equal("APT-20261004-1234", AppointmentReference.Build(Oct4, 1234));
        Assert.NotEqual("APT-20261004-1234", AppointmentReference.BuildNaive(Oct4, 1234));
    }

    [Fact]
    public void Translations_EveryEnglishKeyExistsInArabic()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(root, "en.json"), """{ "A": "a", "B": "b" }""");
        File.WriteAllText(Path.Combine(root, "ar.json"), """{ "A": "أ", "B": "ب" }""");

        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "en.json")))!;
        var ar = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "ar.json")))!;

        Assert.Empty(en.Keys.Except(ar.Keys));
    }
}
