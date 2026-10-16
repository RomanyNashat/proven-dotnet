---
name: pii-masking
description: PII/PHI masking for .NET healthcare: Serilog masking (destructuring policy + pattern operators for national ID, mobile, MRN), classified logging with redaction before any provider, [Sensitive] strategies, keyed tokenization. Code tested in CI.
version: 1.5.0
---

# PII Masking Patterns

The code in this skill is compiled and tested in CI (`tests/SkillSamples.Tests/PiiMasking`) against
Serilog and `Serilog.Enrichers.Sensitive` 2.1.0 (MIT). The tests log real-looking values and assert they
don't reach the output, which is the test the rules below ask every service to have.

## Two layers

1. **Objects** logged with `{@Patient}`: a destructuring policy masks properties by name or by a
   `[Sensitive]` attribute.
2. **Strings and scalars** (`{Input}`, an exception message logged as a property): pattern operators find national IDs,
   mobiles, MRNs, emails and card numbers inside the text.

Neither layer alone is enough. A property called `Notes` can contain a national ID; a pattern can't tell
a diagnosis from any other text.

**Neither layer touches the exception.** `logger.Error(ex, ...)` hands the exception to the sink as it is,
and its message reaches Elastic unmasked. Tested as a story: a failed booking's note is masked, the
exception text in the same event still carries the national ID. Where an exception can echo patient data
(a downstream error, a validation message), log its type, or its message as a property so the operators
see it.

**Arabic-Indic digits.** Arabic keyboards type `١٠٨٩٢٣٤٥٦٧`. `\d` matches those digits, but a pattern
that starts with `[12]` doesn't, and the ID went through whole. Found by a production test; the operators
below list both.

## Classified logging: redact by what the value is

The two layers above work on names and patterns, after the fact. A third, for code you write now: mark
the parameter with what it is, and the logging pipeline redacts it before any provider sees it, wherever
the line goes (`Microsoft.Extensions.Compliance.Redaction` with `Microsoft.Extensions.Telemetry`).

<!-- sample: tests/SkillSamples.Tests/PiiMasking/ClinicData.cs -->
```csharp
/// <summary>The service's data classes. A redactor is chosen per class, at startup.</summary>
public static class ClinicData
{
    public const string Taxonomy = "Clinic";

    /// <summary>Identifies a patient on its own: national ID, mobile, MRN.</summary>
    public static DataClassification PatientIdentifier => new(Taxonomy, nameof(PatientIdentifier));
}

/// <summary>Marks a log parameter or a property as <see cref="ClinicData.PatientIdentifier"/>.</summary>
public sealed class PatientIdentifierAttribute() : DataClassificationAttribute(ClinicData.PatientIdentifier);
```

<!-- sample: tests/SkillSamples.Tests/PiiMasking/BookingLog.cs -->
```csharp
public static partial class BookingLog
{
    // The classification travels with the parameter: wherever this line is written, the national ID goes
    // through the redactor chosen for PatientIdentifier, before any provider sees it.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Booking {BookingId} refused for {NationalId}: {Reason}")]
    public static partial void BookingRefused(ILogger logger, int bookingId, [PatientIdentifier] string nationalId, string reason);

    // Every property of the request becomes a field. Only the classified ones are redacted, so a property
    // someone adds later without the attribute (Mobile here) goes out in clear.
    [LoggerMessage(Level = LogLevel.Information, Message = "Booking requested")]
    public static partial void BookingRequested(ILogger logger, [LogProperties] BookingRequest request);
}
```

<!-- sample: tests/SkillSamples.Tests/PiiMasking/RedactionSetup.cs -->
```csharp
public static class RedactionSetup
{
    // Redaction runs in the logger factory that EnableRedaction installs. Serilog has to be a provider
    // under it (AddSerilog). UseSerilog() replaces the factory, and the classified fields never reach the
    // redactor.
    public static IServiceCollection AddClassifiedLogging(this IServiceCollection services, Action<ILoggingBuilder> providers) => services
        .AddRedaction(redaction => redaction.SetRedactor<StarRedactor>(ClinicData.PatientIdentifier))
        .AddLogging(logging =>
        {
            logging.EnableRedaction();
            providers(logging);
        });
}
```

Tested with `FakeLogger` and with Serilog:
- The classified parameter is `****` in the message and in the field; the booking id and the reason are
  untouched.
- **It covers only what is classified.** The same ID through an ordinary
  `logger.LogWarning("... {NationalId}", id)` goes through in clear. Keep the pattern operators: they're
  the net for every line nobody marked.
- **Story: Serilog must be a provider, not the factory.** `builder.Logging.AddSerilog(logger)` keeps the
  redacting factory, and Elastic gets `****`. `UseSerilog()`, `builder.Services.AddSerilog()` (the
  hosting package's, not the logging builder's) and registering `SerilogLoggerFactory` all make Serilog
  the factory, and in a real host the order with `EnableRedaction()` decides how it goes wrong
  (`SerilogOrderTests`):
  - **`EnableRedaction()` after it:** the redacting factory goes back over Serilog's, and Serilog isn't a
    provider under it. **Nothing logged through `ILogger` reaches Serilog**: the service's logs stop
    arriving, and nothing says so.
  - **`EnableRedaction()` before it:** Serilog's factory wins, the redactor never runs, and the classified
    field silently vanishes from the line. It doesn't leak, but nobody can search by it either.

  `known-traps` checks for it (`serilog-factory-with-redaction`).
- **`HttpClient` logs a failed request's exception, message and all.** `IHttpClientFactory`'s default
  logging writes "HTTP request failed after {ElapsedMilliseconds}ms" with the exception attached, and no
  redactor or masking operator reads exception text. A provider whose error repeats the number puts it in
  the log even when the service's own catch logs only an id. On a client whose errors can carry patient
  data, call `.RemoveAllLoggers()` on its builder and log your own line with the status code. Tested
  (`HttpClientLoggingTests`); found by the fix eval in `tests/evals/fix-hard`.
- **`[LogProperties]` logs every property, today's and tomorrow's.** Only the classified ones are redacted
  (a record's constructor parameter counts). Tested: a `Mobile` added without `[PatientIdentifier]` goes
  out as it is. Use it only on types where every patient field is classified, and review additions to
  those types like a log line.
- The redactor is chosen per class at startup. `StarRedactor` here; the library's `ErasingRedactor` is
  the fallback for a class with none. To correlate lines about one patient without the ID, use a keyed
  hash (the library's HMAC redactor, still experimental, or the tokenizer below).

## Setup

<!-- sample: tests/SkillSamples.Tests/PiiMasking/LoggingSetup.cs -->
```csharp
public static class LoggingSetup
{
    // Two layers: the destructuring policy masks {@Objects} by property; the enricher catches PII inside
    // scalar values and strings by pattern. Setting MaskingOperators replaces the library's defaults, so
    // the ones still wanted (email, IBAN, card) are listed again.
    public static LoggerConfiguration AddPiiMasking(this LoggerConfiguration config) => config
        .Destructure.With<SensitiveFieldDestructuringPolicy>()
        .Enrich.WithSensitiveDataMasking(options =>
        {
            options.MaskValue = Masking.Full;
            options.MaskingOperators =
            [
                new EmailAddressMaskingOperator(),
                new IbanMaskingOperator(),
                new CreditCardMaskingOperator(),
                new SaudiNationalIdMaskingOperator(),
                new SaudiMobileMaskingOperator(),
                new MedicalRecordNumberMaskingOperator(),
            ];
        });
}
```

In a service, with classified logging (Serilog as a provider under the redacting factory, never
`UseSerilog()`, see the story above):
```csharp
builder.Logging.ClearProviders();
builder.Logging.EnableRedaction();
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .AddPiiMasking()
    .CreateLogger(), dispose: true);
```
Register it before anything logs.

## Pattern operators

<!-- sample: tests/SkillSamples.Tests/PiiMasking/MaskingOperators.cs -->
```csharp
// Saudi national ID or iqama: 10 digits starting with 1 or 2. Keep the last four only: the first digit
// says citizen or resident, which is itself personal data. Also matches 10-digit epoch seconds; masking
// those too is the safe side of the trade. Arabic keyboards type Arabic-Indic digits (١٠٨٩...): \d
// matches them, but the leading [12] must list them too, or the whole ID goes through unmasked.
public sealed class SaudiNationalIdMaskingOperator() : RegexMaskingOperator(@"(?<!\d)[12\u0661\u0662\u06F1\u06F2]\d{9}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "******" + match.Value[^4..];
}

// Saudi mobile: 05XXXXXXXX, 5XXXXXXXX, +9665XXXXXXXX or 009665XXXXXXXX, in Western or Arabic-Indic digits.
public sealed class SaudiMobileMaskingOperator() : RegexMaskingOperator(@"(?<!\d)(?:\+966|00966|[0\u0660\u06F0])?[5\u0665\u06F5]\d{8}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "*******" + match.Value[^2..];
}

public sealed class MedicalRecordNumberMaskingOperator()
    : RegexMaskingOperator(@"MRN[-:]?\s*\d{6,12}", RegexOptions.Compiled | RegexOptions.IgnoreCase)
{
    protected override string PreprocessMask(string mask, Match match) => "MRN:" + Masking.Full;
}
```

The library's extension point is `PreprocessMask(mask, match)`; it returns the replacement for one match.

## Masking strategies and `[Sensitive]`

<!-- sample: tests/SkillSamples.Tests/PiiMasking/Masking.cs -->
```csharp
public enum MaskingStrategy
{
    Full,       // "***MASKED***"
    Email,      // "r***@example.com"
    Phone,      // "*******89"
    LastFour,   // "******4567"
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute(MaskingStrategy strategy = MaskingStrategy.Full) : Attribute
{
    public MaskingStrategy Strategy { get; } = strategy;
}

public static class Masking
{
    public const string Full = "***MASKED***";

    // A value too short to partly show is masked completely: showing "all but the last four" of a
    // four-character value shows all of it.
    public static string Mask(string? value, MaskingStrategy strategy) => value switch
    {
        null => Full,
        _ => strategy switch
        {
            MaskingStrategy.Email => MaskEmail(value),
            MaskingStrategy.Phone => KeepLast(value, 2),
            MaskingStrategy.LastFour => KeepLast(value, 4),
            _ => Full,
        },
    };

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at < 1 || at == email.Length - 1 ? Full : $"{email[0]}***{email[at..]}";
    }

    private static string KeepLast(string value, int visible) =>
        value.Length <= visible * 2 ? Full : new string('*', value.Length - visible) + value[^visible..];
}
```

Use it on DTOs:

```csharp
public sealed record PatientDto(
    int Id,
    string NameEn,
    [property: Sensitive(MaskingStrategy.LastFour)] string NationalId,
    [property: Sensitive(MaskingStrategy.Email)] string Email,
    string? Diagnosis);   // masked by name, no attribute needed
```

## The destructuring policy

<!-- sample: tests/SkillSamples.Tests/PiiMasking/SensitiveFieldDestructuringPolicy.cs -->
```csharp
// Masks sensitive properties when an object is logged with {@Object}. Types with nothing sensitive are
// left to Serilog's default handling, and the per-type plan is computed once.
public sealed class SensitiveFieldDestructuringPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Secret", "Token", "ApiKey", "AccessToken", "RefreshToken", "Authorization",
        "CardNumber", "Cvv", "NationalId", "Iqama", "PhoneNumber", "MobileNumber", "Mobile", "Email",
        "DateOfBirth", "Dob", "MedicalRecordNumber", "Mrn", "Diagnosis", "Prescription",
    };

    private static readonly ConcurrentDictionary<Type, Plan?> Plans = new();

    public bool TryDestructure(
        object value, ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        var plan = Plans.GetOrAdd(value.GetType(), BuildPlan);
        if (plan is null)
        {
            result = null;
            return false;
        }

        var properties = new List<LogEventProperty>(plan.Properties.Length);
        foreach (var (property, strategy) in plan.Properties)
        {
            var propertyValue = property.GetValue(value);
            LogEventPropertyValue logged = strategy is { } s
                ? new ScalarValue(Masking.Mask(propertyValue?.ToString(), s))
                : propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true);
            properties.Add(new LogEventProperty(property.Name, logged));
        }

        result = new StructureValue(properties, plan.TypeTag);
        return true;
    }

    private static Plan? BuildPlan(Type type)
    {
        // Scalars, framework types (DateTime, Guid, Uri...) and collections keep Serilog's own handling.
        // Anonymous types have no namespace and are checked like your own types.
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type)
            || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) == true)
        {
            return null;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => (Property: p, Strategy: StrategyFor(p)))
            .ToArray();
        return properties.Any(p => p.Strategy is not null) ? new Plan(type.Name.StartsWith("<>", StringComparison.Ordinal) ? null : type.Name, properties) : null;
    }

    private static MaskingStrategy? StrategyFor(PropertyInfo property) =>
        property.GetCustomAttribute<SensitiveAttribute>()?.Strategy
        ?? (SensitiveNames.Contains(property.Name) ? MaskingStrategy.Full : null);

    private sealed record Plan(string? TypeTag, (PropertyInfo Property, MaskingStrategy? Strategy)[] Properties);
}
```

What a naive version gets wrong, and this one is tested against: it destructures `DateTime`, `Guid` and
lists into their internal properties (a `List<T>` logs its `Capacity`), it throws on indexers, it runs
reflection on every log call, and it misses anonymous objects (`new { NationalId = ... }`), which have no
namespace.

## Tokenization: PII out of the database

<!-- sample: tests/SkillSamples.Tests/PiiMasking/PiiTokenizer.cs -->
```csharp
public interface ITokenRepository
{
    Task StoreAsync(string token, byte[] encrypted, string fieldType, CancellationToken ct);   // upsert
    Task<byte[]?> GetAsync(string token, CancellationToken ct);
}

// Replaces a PII value with a token. The same value always gets the same token, so lookups by value work.
// The token is an HMAC with a secret key, not a plain hash: a national ID has only 10^10 values, so a
// plain SHA-256 of one is reversed by hashing them all.
public sealed class PiiTokenizer(byte[] tokenKey, IEncryptor encryptor, ITokenRepository tokens)
{
    public async Task<string> TokenizeAsync(string plaintext, string fieldType, CancellationToken ct)
    {
        var token = Token(plaintext, fieldType);
        var encrypted = encryptor.Encrypt(Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(token));
        await tokens.StoreAsync(token, encrypted, fieldType, ct);
        return token;
    }

    public async Task<string?> DetokenizeAsync(string token, CancellationToken ct)
    {
        var encrypted = await tokens.GetAsync(token, ct);
        return encrypted is null
            ? null
            : Encoding.UTF8.GetString(encryptor.Decrypt(encrypted, Encoding.UTF8.GetBytes(token)));
    }

    public string Token(string plaintext, string fieldType) =>
        "tok_" + Convert.ToHexStringLower(HMACSHA256.HashData(tokenKey, Encoding.UTF8.GetBytes($"{fieldType}:{plaintext}")))[..32];
}
```

The token key and the encryption key are different keys, both from the vault (`secret-management`). The
ciphertext is bound to its token as associated data, so swapping rows fails to decrypt. Log every
detokenize call (who, which token, why), never the value.

## See also
- **`healthcare-compliance`**: what counts as PHI and the audit obligations; this skill is the mechanics.
- **`observability`**: logs and traces are the most common accidental leak; APM headers too.
- **`encryption-patterns`**: the `IEncryptor` used above.
- **`fhir`** *(per-project)*: FHIR resources carry PHI by definition.

## Rules
- Never log PII: passwords, tokens, national IDs, health data, card numbers, mobiles, emails.
- Register masking before any log statement runs.
- Mark PII properties on DTOs with `[Sensitive]`, or give them a name the policy knows.
- New log lines with patient identifiers: a `[LoggerMessage]` method with the parameter classified, and
  Serilog added as a provider (`AddSerilog`), never as the factory (`UseSerilog`).
- Every service has a test that logs a known PII value and asserts it's masked (like the tests behind
  this skill).
- Tokens are keyed (HMAC), never a plain hash of the value.
- Log every detokenization.
