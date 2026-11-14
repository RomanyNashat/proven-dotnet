using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicQueue.Tests;

public sealed class ServiceBehindNginxTests
{
    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("203.0.113.9, 10.42.0.3")]
    [InlineData("1.2.3.4, 203.0.113.9, 10.42.0.3")]
    public async Task WhoAmI_RequestFromNginxPod_ReturnsTheClientNotTheProxy(string forwardedFor)
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        var caller = await response.Content.ReadFromJsonAsync<JsonElement>();

        caller.GetProperty("clientIp").GetString().Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task Clinics_FirstRequestAfterStart_SeesEveryClinic()
    {
        using var factory = new ClinicQueueFactory
        {
            ClinicSource = new FixedClinicSource([new ClinicEntry(1, "Cardiology"), new ClinicEntry(2, "Dermatology")], TimeSpan.FromSeconds(3)),
        };
        using var client = factory.CreateAuthenticatedClient();

        var clinics = await client.GetFromJsonAsync<JsonElement>("/clinics");

        clinics.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Clinics_ShapeIsIdAndName()
    {
        using var factory = new ClinicQueueFactory { ClinicSource = new FixedClinicSource([new ClinicEntry(3, "Paediatrics")]) };
        using var client = factory.CreateAuthenticatedClient();

        var json = await client.GetStringAsync("/clinics");

        json.Should().Be("""[{"id":3,"name":"Paediatrics"}]""");
    }

    [Fact]
    public async Task Queue_CachedSnapshot_ServedWithoutTheDatabase()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var snapshot = new QueueSnapshot { ClinicId = 4, Day = new DateTime(2026, 11, 20), Positions = [1, 2, 3] };
        await cache.SetAsync("queue:4:2026-11-20", JsonSerializer.SerializeToUtf8Bytes(snapshot));
        using var factory = new ClinicQueueFactory { Cache = cache };
        using var client = factory.CreateAuthenticatedClient();

        var json = await client.GetStringAsync("/clinics/4/queue?day=2026-11-20");

        json.Should().Be("""{"clinicId":4,"day":"2026-11-20","positions":[1,2,3]}""");
    }

    [Fact]
    public async Task Queue_UnexpectedError_Returns500AndLogsTheException()
    {
        using var factory = new ClinicQueueFactory { Cache = new BrokenCache() };
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/clinics/4/queue?day=2026-11-20");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        factory.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Queue_UnexpectedError_ResponseHasNoDetail()
    {
        using var factory = new ClinicQueueFactory { Cache = new BrokenCache() };
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/clinics/4/queue?day=2026-11-20");
        var text = await response.Content.ReadAsStringAsync();

        text.Should().NotContain("cache is down");
    }

    [Fact]
    public async Task Clinics_NoToken_Returns401()
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/clinics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_NoToken_Returns200()
    {
        using var factory = new ClinicQueueFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class BrokenCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("cache is down");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache is down");

        public void Refresh(string key) => throw new InvalidOperationException("cache is down");

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache is down");

        public void Remove(string key) => throw new InvalidOperationException("cache is down");

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache is down");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("cache is down");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("cache is down");
    }
}
