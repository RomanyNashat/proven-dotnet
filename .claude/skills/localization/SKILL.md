---
name: localization
description: Arabic/English localization for .NET services — x-language header, resx or JSON text (both supported), the ar-SA Hijri and digit traps, ICU/tzdata on Alpine images, NameAr/NameEn data (nvarchar on SQL Server), Arabic search, SMS. Code tested in CI, the data part on PostgreSQL and SQL Server.
---

# Localization (Arabic / English)

Callers send `x-language: ar` or `en`. Every service reads it itself, and services localize their text
with either **resx** or **JSON** files. Both are supported here; neither is forced.

Three rules carry most of the value:
1. **The header changes the text, never the formatting.** `ar-SA` defaults to the Hijri calendar and
   Arabic number separators; if it becomes the formatting culture, dates and numbers break.
2. **Follow the service's existing pattern** (resx or JSON). Never mix the two in one service.
3. **Know whether the runtime has ICU.** Many teams' images are Alpine, which ships without ICU and
   tzdata. resx needs ICU; JSON doesn't.

## 1. The traps (measured on .NET 10, and tested in CI)

| What | What happens | Fix |
|---|---|---|
| `new CultureInfo("ar-SA")` | Calendar is **Umm al-Qura (Hijri)**: `DateTime.ToString()` gives `23/4/1448 بعد الهجرة`; `DateTime.Parse("2026-10-04")` **fails** | Never make `ar-SA` the formatting culture (§3) |
| Numbers under Arabic cultures | `1234.5.ToString("N2")` gives `1٬234٫50`; parsing `"1234.50"` back can fail | Same: formatting culture stays `en-US`; machine values use `InvariantCulture` |
| Alpine or chiseled image | No ICU, globalization-invariant mode: creating `ar-SA` **throws** `CultureNotFoundException`. With resx set up, the service won't start | §4 guard; or use JSON (§5) |
| Same images, time zones | No tzdata: `FindSystemTimeZoneById("Asia/Riyadh")` **throws** | §7 fixed-offset fallback |
| `CultureInfo("ar")` (neutral) | Gregorian calendar, but still Arabic separators | Don't rely on it for formatting either |

**API values are never localized.** JSON bodies, query strings, logs, SQL, cache keys and message
payloads stay culture-free: ISO 8601 UTC dates, invariant numbers. System.Text.Json already does this;
string interpolation and `ToString()` don't, so any of those on a machine value needs
`CultureInfo.InvariantCulture`.

## 2. Reading the header (both patterns)

<!-- sample: tests/SkillSamples.Tests/Localization/LanguageHeader.cs -->
```csharp
public static class LanguageHeader
{
    public const string Name = "x-language";
    public const string Arabic = "ar";
    public const string English = "en";

    // "ar", "ar-SA", "AR" → ar. Anything else, or no header → en.
    public static string Parse(string? value) =>
        value is not null && value.Trim().StartsWith(Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
}
```

## 3. Choosing the pattern

**Existing service: detect, then follow.**
- **resx:** `Resources/*.resx`, `AddLocalization`, `IStringLocalizer<T>`, `UseRequestLocalization`.
- **JSON:** `*.json` translation files (often `i18n/`, `Localization/`, `Translations/`) and a custom
  lookup class.

If a service has both, say so and ask which one is the keeper. Don't add a third.

**New service: ask.** The difference that matters:

| | resx (Pattern A) | JSON (Pattern B) |
|---|---|---|
| Needs ICU in the image | **Yes** | No: looks up by language code, not by culture |
| Validation messages | Plugs into `IStringLocalizer` and DataAnnotations directly | Pass text in yourself (`WithMessage`) |
| Where the text lives | Compiled into the assembly | Files today; **swappable source**, e.g. a central store later |
| Who can edit it | Developers (rebuild) | Anyone who can edit JSON (no rebuild once it's remote) |

## 4. Pattern A — resx

<!-- sample: tests/SkillSamples.Tests/Localization/XLanguageCultureProvider.cs -->
```csharp
// The header picks the UI culture (text) only. The formatting culture is en-US for every request.
public sealed class XLanguageCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var ui = LanguageHeader.Parse(httpContext.Request.Headers[LanguageHeader.Name]) == LanguageHeader.Arabic ? "ar-SA" : "en-US";
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture: "en-US", uiCulture: ui));
    }
}
```

```csharp
// Program.cs
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(culture: "en-US", uiCulture: "en-US");
    o.SupportedCultures = [new CultureInfo("en-US")];                             // formatting
    o.SupportedUICultures = [new CultureInfo("en-US"), new CultureInfo("ar-SA")];  // text
    o.RequestCultureProviders = [new XLanguageCultureProvider()];
});
// ...
GlobalizationGuard.RequireArabicCulture();   // before the first request, see below
app.UseRequestLocalization();                // before the endpoints
```

Files: `Resources/SharedResource.resx` (English) and `Resources/SharedResource.ar.resx`. Name the
Arabic file with the neutral **`ar`**: `ar-SA` falls back to it. Use it through
`IStringLocalizer<SharedResource> text` → `text["AppointmentNotFound", id]`.

**Gotcha: the assembly name differs from the root namespace.** The localizer finds the resources by
assembly name, so with an assembly `payments.api` and namespace `Payments.Api` it silently returns the
key instead of the text. Fix: `[assembly: RootNamespace("Payments.Api")]`.

**The ICU guard.** Without ICU, creating the cultures throws during startup with an error that doesn't
say why. This guard turns that into a clear message:

<!-- sample: tests/SkillSamples.Tests/Localization/GlobalizationGuard.cs -->
```csharp
public static class GlobalizationGuard
{
    public static void RequireArabicCulture()
    {
        try { _ = CultureInfo.GetCultureInfo("ar-SA", predefinedOnly: true); }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                "This service localizes with resx and needs the ar-SA culture, but the runtime has no ICU " +
                "(globalization-invariant mode). Add ICU to the runtime image, or use the JSON pattern.", ex);
        }
    }
}
```

Repos don't hold a Dockerfile; the image comes from your pipeline. If the guard
fires, the fix is ICU in that pipeline's runtime image (`icu-libs`, with invariant mode off), or the
JSON pattern. It is not a Dockerfile in the repo.

**Validation messages:** FluentValidation: `.WithMessage(_ => text["Field.Required"])`. Controllers
with DataAnnotations: `AddControllers().AddDataAnnotationsLocalization()`.

## 5. Pattern B — JSON

Text is looked up by language code (`"ar"`/`"en"`), never through `CultureInfo`, so it works on an
image with no ICU. The source is an interface: a file today, a central store later, with the same
callers.

<!-- sample: tests/SkillSamples.Tests/Localization/JsonTranslations.cs -->
```csharp
public interface ITranslationSource
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken cancellationToken);
}

// i18n/{language}.json: { "Errors.AppointmentNotFound": "Appointment {0} was not found." }
public sealed class FileTranslationSource(IHostEnvironment environment) : ITranslationSource
{
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, "i18n", $"{language}.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
               ?? new Dictionary<string, string>();
    }
}

// Each language loads once and stays in memory. Missing Arabic → English; missing key → the key itself.
public sealed class TranslationCatalog(ITranslationSource source)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> _languages = new();

    public async Task<string> GetAsync(string key, string language, CancellationToken cancellationToken = default)
    {
        var texts = await _languages
            .GetOrAdd(language, lang => new Lazy<Task<IReadOnlyDictionary<string, string>>>(() => source.LoadAsync(lang, CancellationToken.None)))
            .Value;
        if (texts.TryGetValue(key, out var text)) return text;
        return language == LanguageHeader.English ? key : await GetAsync(key, LanguageHeader.English, cancellationToken);
    }
}

public sealed class RequestLanguage(IHttpContextAccessor accessor)
{
    public string Value => LanguageHeader.Parse(accessor.HttpContext?.Request.Headers[LanguageHeader.Name]);
}
```

```csharp
// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITranslationSource, FileTranslationSource>();
builder.Services.AddSingleton<TranslationCatalog>();
builder.Services.AddScoped<RequestLanguage>();
// csproj: <Content Update="i18n/*.json" CopyToOutputDirectory="PreserveNewest" />
```

Fill placeholders with `string.Format(CultureInfo.InvariantCulture, text, args)`. A remote source
(central store, Redis, config service) implements the same `ITranslationSource`. It needs a cache with
expiry or a reload signal, plus a local file fallback so a store outage doesn't take the text with it.

## 6. Dates a person reads

- **API:** ISO 8601 UTC, always. The frontend formats.
- **Text the backend writes itself** (SMS, push, email, PDF):
  - Arabic month names on the Gregorian calendar (`4 أكتوبر 2026`): use a clone of `ar-SA` with the
    calendar switched.
  - **Hijri, only where the business asks for it** (`23 ربيع الآخر 1448`): use `ar-SA` explicitly, named
    as Hijri so nobody uses it by accident.

<!-- sample: tests/SkillSamples.Tests/Localization/Cultures.cs -->
```csharp
public static class Cultures
{
    public static readonly CultureInfo ArabicGregorian = CreateArabicGregorian();
    public static readonly CultureInfo ArabicHijri = CultureInfo.ReadOnly(new CultureInfo("ar-SA"));

    private static CultureInfo CreateArabicGregorian()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        return CultureInfo.ReadOnly(culture);
    }
}
```

`date.ToString("d MMMM yyyy", Cultures.ArabicGregorian)` gives `4 أكتوبر 2026`; with `ArabicHijri`, `23 ربيع الآخر 1448`. Both are tested, and both need ICU.

On a JSON-pattern service without ICU, write the date as `yyyy-MM-dd` or keep Arabic
month names in the translation file.

## 7. Saudi time without tzdata

<!-- sample: tests/SkillSamples.Tests/Localization/RiyadhTime.cs -->
```csharp
// Saudi Arabia is UTC+3 all year, with no daylight saving, so a fixed offset is exact.
public static class RiyadhTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh"); }
    }
}
```
Store UTC (`timestamptz` on PostgreSQL, `datetime2(3)` holding UTC on SQL Server); convert to Riyadh time only for display or for business rules about the
local day ("before midnight Riyadh time"). Get "now" from `TimeProvider`.

## 8. Bilingual data: `NameAr` / `NameEn`

The shape here is **two bounded columns**, both under the column rules (explicit length), plus the search
column from §9. Tested on PostgreSQL and SQL Server (`tests/SkillSamples.Tests/Localization/`):

<!-- sample: tests/SkillSamples.Tests/Localization/HospitalConfiguration.cs -->
```csharp
// A service has one engine and keeps one branch; both are here so CI checks both.
public sealed class HospitalConfiguration(bool sqlServer) : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> builder)
    {
        builder.ToTable("hospitals");
        var id = builder.Property(h => h.Id).HasColumnName("id");
        if (sqlServer)
        {
            id.UseIdentityColumn();
        }
        else
        {
            id.UseIdentityAlwaysColumn();
        }

        // IsUnicode() makes nvarchar(n) on SQL Server. A varchar column there stores Arabic as "???" (tested),
        // with no error. PostgreSQL's varchar(n) is UTF-8 and ignores IsUnicode.
        builder.Property(h => h.NameAr).HasColumnName("name_ar").HasMaxLength(200).IsUnicode();
        builder.Property(h => h.NameEn).HasColumnName("name_en").HasMaxLength(200).IsUnicode();
        builder.Property(h => h.NameArSearch).HasColumnName("name_ar_search").HasMaxLength(200).IsUnicode();

        // Prefix search. PostgreSQL needs varchar_pattern_ops for LIKE 'x%' unless the database collation is C;
        // on SQL Server a plain index serves it.
        var search = builder.HasIndex(h => h.NameArSearch).HasDatabaseName("ix_hospitals_name_ar_search");
        if (!sqlServer)
        {
            search.HasOperators("varchar_pattern_ops");
        }
    }
}
```

- **SQL Server: Arabic needs `nvarchar(n)`** (`IsUnicode()`). In a `varchar(n)` column under the default
  collation, every Arabic letter is stored as `?`, with no error and no warning (tested: `مستشفى` comes back
  as `??????`). The same goes for hand-written DDL and Dapper table scripts. PostgreSQL's `varchar(n)` is
  UTF-8 and stores it as is.
- EF maps a string to Unicode by default, so the trap is in scripts and in `IsUnicode(false)` "to save
  space", not in a plain `HasMaxLength`.

Pick the column **in the query**, so only one language leaves the database:

<!-- sample: tests/SkillSamples.Tests/Localization/HospitalSearch.cs -->
```csharp
public sealed record HospitalItem(int Id, string Name);

public static class HospitalSearch
{
    /// <summary>
    /// Matches what the user typed against the normalized column, and returns the name in the request's
    /// language only: the CASE runs in SQL, so the other language never leaves the database.
    /// </summary>
    public static Task<List<HospitalItem>> SearchAsync(
        this HospitalsDbContext db, string language, string typed, CancellationToken ct)
    {
        var term = ArabicSearch.NormalizeForSearch(typed);
        return db.Hospitals.AsNoTracking()
            .Where(h => h.NameArSearch.StartsWith(term))
            .OrderBy(h => h.Id)
            .Select(h => new HospitalItem(h.Id, language == LanguageHeader.Arabic ? h.NameAr : h.NameEn))
            .ToListAsync(ct);
    }
}
```

Dapper: `SELECT id, CASE WHEN @lang = 'ar' THEN name_ar ELSE name_en END AS name ...`. MongoDB: the same
two fields (`nameAr`, `nameEn`) and a projection. When one language is optional, fall back to the other
in the projection rather than returning null.

## 9. Searching Arabic text

People type the same word several ways: أحمد/احمد, مستشفى/مستشفي, with or without tashkeel. A plain
`ILIKE`, collation or index won't match them. Store a **normalized search column** next to the name,
filled by the app on every write, and normalize the search input with the same function:

<!-- sample: tests/SkillSamples.Tests/Localization/ArabicSearch.cs -->
```csharp
public static class ArabicSearch
{
    public static string NormalizeForSearch(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ') continue;   // tashkeel, tatweel
            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => ch,
            });
        }
        return builder.ToString().Trim();
    }
}
```

`"أَحْمَد"` → `"احمد"`, `"مستشفى المَدينة"` → `"مستشفي المدينه"` (tested).
Column: `name_ar_search`, the same type as the name (`nvarchar(n)` on SQL Server, `varchar(n)` on
PostgreSQL), filled by the entity whenever the name changes, and indexed for prefix search: a b-tree with
`varchar_pattern_ops` on PostgreSQL, a plain index on SQL Server (both in the configuration above).
Tested on both engines: "مستشفي احمد" finds "مستشفى أحمد التخصصي". It's derived data, so a backfill
script fills it for existing rows. MongoDB: the same field
plus an index. This is deterministic, unlike collation strength settings, and behaves the same on
every engine.

## 10. Notifications and SMS

- Keep one template per language, keyed the same way as error text, and pick by the recipient's stored
  language, not the language of the request that triggered it (a background job has no request).
- **Arabic SMS costs more:** a single Arabic message holds **70 characters** (UCS-2), and each part of
  a long one holds 67. English holds 160, and 153 per part. Keep Arabic templates short, and count parts
  before changing one.
- Mixed Arabic text with Latin digits or codes reads right-to-left with left-to-right runs. Check how the
  final text renders on a phone before shipping a template.

## 11. Logs and OpenAPI

- **Logs are never localized**: English messages, invariant values. With the culture split in §4 the
  formatting culture is already `en-US`; a Serilog sink can also pin
  `formatProvider: CultureInfo.InvariantCulture`.
- **OpenAPI:** document `x-language` once, as a global header parameter, not on every operation.

## 12. Testing

Run the culture-sensitive code under `ar-SA` on purpose. On a developer machine it never is, which is
why these bugs reach production.

<!-- sample: tests/SkillSamples.Tests/Localization/CultureScope.cs -->
```csharp
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
```

The trap it catches, tested: `$"APT-{date:yyyyMMdd}"` under `ar-SA` writes a **Hijri** date, while
`string.Create(CultureInfo.InvariantCulture, $"APT-{date:yyyyMMdd}")` stays `APT-20261004`.

In a service test (FluentAssertions, as in the rules):

```csharp
[Fact]
public void BuildReference_UnderArabicCulture_StaysGregorianAndLatin()
{
    using var _ = new CultureScope("ar-SA");
    var reference = AppointmentReference.Build(new DateTime(2026, 10, 4), 1234);
    reference.Should().Be("APT-20261004-1234");
}
```
- Each localized endpoint gets a test with `x-language: ar` and one with `en`.
- Pattern B: one test that every key in `en.json` exists in `ar.json`.

## 13. Review checklist (used by `code-reviewer`)

- `ToString()`, `Parse`, `TryParse` or interpolation on a date or number that reaches an API, log, SQL,
  cache key or file, without `CultureInfo.InvariantCulture` or an explicit format.
- `ar-SA` (or the header) used as the formatting culture rather than the UI culture.
- A Hijri date produced anywhere the business didn't ask for one.
- `FindSystemTimeZoneById` with no fallback, or `DateTime.Now`.
- resx and JSON both in one service, or a third pattern added.
- resx in a service whose runtime has no ICU, without the guard.
- A bilingual column without `HasMaxLength`, or both languages returned when the caller wanted one.
- Arabic search on the raw column instead of a normalized one.
