using System.Security.Cryptography;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SkillSamples.Encryption;
using Xunit;

namespace SkillSamples.PiiMasking;

public sealed record PatientDto(
    int Id,
    string NameEn,
    [property: Sensitive(MaskingStrategy.LastFour)] string NationalId,
    [property: Sensitive(MaskingStrategy.Email)] string Email,
    string? Diagnosis,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Tags);

public sealed record ClinicDto(int Id, string NameEn);

public sealed class CollectingSink : ILogEventSink
{
    public List<string> Rendered { get; } = [];

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        logEvent.RenderMessage(writer);
        Rendered.Add(writer.ToString());
    }
}

public sealed class InMemoryTokens : ITokenRepository
{
    public Dictionary<string, byte[]> Rows { get; } = [];

    public Task StoreAsync(string token, byte[] encrypted, string fieldType, CancellationToken ct)
    {
        Rows[token] = encrypted;
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string token, CancellationToken ct) =>
        Task.FromResult(Rows.TryGetValue(token, out var v) ? v : null);
}

public sealed class PiiMaskingTests
{
    private static (Logger Logger, CollectingSink Sink) NewLogger()
    {
        var sink = new CollectingSink();
        return (new LoggerConfiguration().AddPiiMasking().WriteTo.Sink(sink).CreateLogger(), sink);
    }

    [Theory]
    [InlineData("1089234567", MaskingStrategy.LastFour, "******4567")]
    [InlineData("1234", MaskingStrategy.LastFour, "***MASKED***")]
    [InlineData("0551234589", MaskingStrategy.Phone, "********89")]
    [InlineData("055", MaskingStrategy.Phone, "***MASKED***")]
    [InlineData("rana@example.com", MaskingStrategy.Email, "r***@example.com")]
    [InlineData("@example.com", MaskingStrategy.Email, "***MASKED***")]
    [InlineData("not-an-email", MaskingStrategy.Email, "***MASKED***")]
    [InlineData("anything", MaskingStrategy.Full, "***MASKED***")]
    public void Mask_ShortOrOddValues_NeverLeak(string value, MaskingStrategy strategy, string expected) =>
        Assert.Equal(expected, Masking.Mask(value, strategy));

    [Fact]
    public void Destructured_SensitiveProperties_AreMasked_OthersKeepTheirShape()
    {
        var (logger, sink) = NewLogger();
        var patient = new PatientDto(7, "Rana", "1089234567", "rana@example.com", "J45.909",
            new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero), ["vip", "follow-up"]);

        logger.Information("Created {@Patient}", patient);
        logger.Dispose();

        var line = Assert.Single(sink.Rendered);
        Assert.DoesNotContain("1089234567", line);
        Assert.DoesNotContain("rana@example.com", line);
        Assert.DoesNotContain("J45.909", line);
        Assert.Contains("******4567", line);
        Assert.DoesNotContain("UtcTicks", line);          // a DateTimeOffset stays a value, not its properties
        Assert.Contains("[\"vip\", \"follow-up\"]", line);
    }

    [Fact]
    public void Destructured_AnonymousObject_IsMaskedByName()
    {
        var (logger, sink) = NewLogger();

        logger.Information("Lookup {@Request}", new { NationalId = "2012345678", ClinicId = 4 });
        logger.Dispose();

        Assert.DoesNotContain("2012345678", sink.Rendered[0]);
        Assert.Contains("ClinicId: 4", sink.Rendered[0]);
    }

    [Fact]
    public void Destructured_TypeWithNothingSensitive_IsLeftAlone()
    {
        var (logger, sink) = NewLogger();

        logger.Information("Clinic {@Clinic}", new ClinicDto(4, "North Clinic"));
        logger.Dispose();

        Assert.Contains("NameEn: \"North Clinic\"", sink.Rendered[0]);
    }

    [Theory]
    [InlineData("1089234567")]
    [InlineData("0551234589")]
    [InlineData("+966551234589")]
    [InlineData("MRN: 00123456")]
    [InlineData("rana@example.com")]
    public void ScalarValues_WithPii_AreMaskedByPattern(string pii)
    {
        var (logger, sink) = NewLogger();

        logger.Information("Caller sent {Input}", $"value {pii} end");
        logger.Dispose();

        Assert.DoesNotContain(pii, sink.Rendered[0]);
        Assert.Contains("end", sink.Rendered[0]);
    }

    [Fact]
    public void ScalarValues_WithoutPii_AreUntouched()
    {
        var (logger, sink) = NewLogger();

        logger.Information("Booked slot {SlotId} at clinic {ClinicId}", 123456, 42);
        logger.Dispose();

        Assert.Equal("Booked slot 123456 at clinic 42", sink.Rendered[0]);
    }

    [Fact]
    public async Task Tokenizer_SameValueSameToken_KeyedNotPlainHash_RoundTrips()
    {
        var store = new InMemoryTokens();
        using var aes = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        var tokenizer = new PiiTokenizer(RandomNumberGenerator.GetBytes(32), aes, store);
        var otherKey = new PiiTokenizer(RandomNumberGenerator.GetBytes(32), aes, store);

        var token = await tokenizer.TokenizeAsync("1089234567", "national_id", default);

        Assert.Equal(token, await tokenizer.TokenizeAsync("1089234567", "national_id", default));
        Assert.NotEqual(token, otherKey.Token("1089234567", "national_id"));   // useless without the key
        Assert.NotEqual(token, tokenizer.Token("1089234567", "phone"));
        Assert.Equal("1089234567", await tokenizer.DetokenizeAsync(token, default));
        Assert.Null(await tokenizer.DetokenizeAsync("tok_unknown", default));
    }

    [Fact]
    public async Task Tokenizer_CiphertextUnderAnotherToken_FailsToDecrypt()
    {
        var store = new InMemoryTokens();
        using var aes = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));
        var tokenizer = new PiiTokenizer(RandomNumberGenerator.GetBytes(32), aes, store);
        var a = await tokenizer.TokenizeAsync("1089234567", "national_id", default);
        var b = await tokenizer.TokenizeAsync("2012345678", "national_id", default);

        store.Rows[b] = store.Rows[a];

        await Assert.ThrowsAnyAsync<CryptographicException>(() => tokenizer.DetokenizeAsync(b, default));
    }
}
