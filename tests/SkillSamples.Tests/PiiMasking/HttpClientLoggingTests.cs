using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.PiiMasking;

public sealed class HttpClientLoggingTests
{
    private const string Mobile = "0551234567";

    // An SMS provider that refuses the number and says which number it refused.
    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"Recipient {Mobile} is not valid");
    }

    private static async Task<IReadOnlyList<FakeLogRecord>> SendAsync(bool removeLoggers)
    {
        var services = new ServiceCollection().AddLogging(logging => logging.AddFakeLogging());
        var client = services.AddHttpClient("sms", c => c.BaseAddress = new Uri("https://sms.example.net/api/v2/"))
            .ConfigurePrimaryHttpMessageHandler(() => new Refusing());
        if (removeLoggers)
        {
            client.RemoveAllLoggers();
        }

        using var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("sms");
        await Assert.ThrowsAsync<HttpRequestException>(() => http.PostAsync("messages", new StringContent("{}")));
        return provider.GetFakeLogCollector().GetSnapshot();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheProviderEchoesTheNumber_HttpClientLogsItUnlessItsLoggersAreRemoved()
    {
        // Given: an SMS client whose provider answers a bad number with an error that repeats the number.
        // The service's own catch logs only the reminder id and the status code.

        // When: the request fails through IHttpClientFactory's default logging
        var logged = await SendAsync(removeLoggers: false);

        // Then: the factory's "request failed" line carries the exception, message and all; no redactor
        // or masking operator reads exception text. With the client's loggers removed, nothing is written.
        Assert.Contains(logged, r => r.Exception?.Message.Contains(Mobile, StringComparison.Ordinal) == true);
        var quiet = await SendAsync(removeLoggers: true);
        Assert.DoesNotContain(quiet, r => (r.Exception?.ToString() ?? "").Contains(Mobile, StringComparison.Ordinal)
            || r.Message.Contains(Mobile, StringComparison.Ordinal));
    }
}
