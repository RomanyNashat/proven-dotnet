using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Tests;

public sealed class ReviewFindingsTests
{
    [Theory]
    [InlineData("8f14e45f-ceea-467f-a9ad-0e5b5f0a3e0c")]
    [InlineData(null)]
    public async Task VisitDay_SubIsNotAPatientId_Returns403(string? sub)
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(sub, DateTime.UtcNow.AddMinutes(10)));

        var response = await client.GetAsync("/visits/1/day");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinics_TokenExpiredTwoMinutesAgo_Returns401()
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("7", DateTime.UtcNow.AddMinutes(-2)));

        var response = await client.GetAsync("/clinics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Queue_ServerCultureIsArSa_CacheKeyStaysGregorian()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var snapshot = new QueueSnapshot { ClinicId = 4, Day = new DateTime(2026, 11, 20), Positions = [1] };
        await cache.SetAsync("queue:4:2026-11-20", JsonSerializer.SerializeToUtf8Bytes(snapshot));
        using var factory = new ClinicQueueFactory { Cache = cache };
        using var client = factory.CreateAuthenticatedClient();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            var response = await client.GetAsync("/clinics/4/queue?day=2026-11-20");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task AnyResponse_HasSecurityHeaders()
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    private static string Token(string? sub, DateTime expires)
    {
        var claims = sub is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim("sub", sub)]);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ClinicQueueFactory.Issuer,
            Audience = "clinic-queue",
            Subject = claims,
            NotBefore = expires.AddMinutes(-30),
            IssuedAt = expires.AddMinutes(-30),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256")), SecurityAlgorithms.HmacSha256),
        });
    }
}
