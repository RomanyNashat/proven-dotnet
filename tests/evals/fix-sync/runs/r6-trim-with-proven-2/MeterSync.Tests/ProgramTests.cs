using System.Net;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MeterSync.Tests;

public sealed class ProgramTests(MeterSyncFactory factory) : IClassFixture<MeterSyncFactory>
{
    [Fact]
    public void PatientDirectory_ResolvedInTwoScopes_IsNotShared()
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<PatientDirectory>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<PatientDirectory>());
    }

    [Fact]
    public async Task MeterVendorClient_ReadingsAsync_KeepsV2InThePath()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var app = WithPrimaryHandler<MeterVendorClient>(handler);
        using var scope = app.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<MeterVendorClient>().ReadingsAsync("SN-1", CancellationToken.None);

        handler.Requests.Should().ContainSingle()
            .Which.Should().Be(new Uri("https://meters.example.net/v2/meters/SN-1/readings"));
    }

    [Fact]
    public async Task SmsClient_GatewayFails_SendsExactlyOnce()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var app = WithPrimaryHandler<SmsClient>(handler);
        using var scope = app.Services.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync("+966500000000", "x", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public void SmsClient_HttpClient_HasTenSecondTimeout() =>
        factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SmsClient))
            .Timeout.Should().Be(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Preflight_FromUnknownOrigin_GetsNoAllowOrigin()
    {
        using var response = await PreflightAsync("https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_FromClinic_GetsAllowOriginWithoutCredentials()
    {
        using var response = await PreflightAsync("https://clinic.example.net");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://clinic.example.net");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/meters", HttpStatusCode.Unauthorized)]
    public async Task AnyResponse_HasSecurityHeaders(string path, HttpStatusCode expectedStatus)
    {
        using var response = await factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(expectedStatus);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; frame-ancestors 'none'");
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/meters");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await factory.CreateClient().SendAsync(request);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithPrimaryHandler<TClient>(HttpMessageHandler handler)
        where TClient : class =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient<TClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
}
