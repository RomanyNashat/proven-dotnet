using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Core;

// Stand-ins for a DbContext (scoped) and a service that wrongly keeps one (singleton).
public sealed class UnitOfWork;
public sealed class ReportCache(UnitOfWork work)
{
    public UnitOfWork Work => work;
}

public sealed class RecordingHandler : HttpMessageHandler
{
    public Uri? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new Shipment(7, "TRK-7"))
        });
    }
}

public sealed class DotnetCoreTests
{
    private static HostApplicationBuilder Builder(string environment, params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)));
        return builder;
    }

    private static async Task<Exception?> StartFailure(IHost host)
    {
        try
        {
            await host.StartAsync();
            await host.StopAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // --- C# features -------------------------------------------------------------------------------

    [Fact]
    public void Money_SameAmountWrittenDifferently_IsEqual()
    {
        var a = new Money(1.0m, "SAR");
        var b = new Money(1.00m, "SAR");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a.ToString(), b.ToString());   // equal values can still print differently: 1.0 and 1.00
    }

    [Fact]
    public void Money_AddDifferentCurrency_Throws()
    {
        Assert.Equal(new Money(15m, "SAR"), new Money(10m, "SAR").Add(new Money(5m, "SAR")));
        Assert.Throws<InvalidOperationException>(() => Money.Zero("SAR").Add(new Money(1m, "USD")));
    }

    [Fact]
    public void PlaceOrder_BodyWithoutLines_FailsToDeserialize()
    {
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PlaceOrder>("""{"customerId":1}""", JsonSerializerOptions.Web));
        Assert.Contains("lines", ex.Message);

        var order = JsonSerializer.Deserialize<PlaceOrder>(
            """{"customerId":1,"lines":[{"productId":2,"quantity":3}]}""", JsonSerializerOptions.Web)!;
        Assert.Equal(new OrderLine(2, 3), order.Lines.Single());
    }

    [Fact]
    public void Product_FieldKeywordSetters_ValidateAndTrim()
    {
        var product = new Product { Name = "  Vitamin D  ", Price = 12.5m };

        Assert.Equal("Vitamin D", product.Name);
        Assert.Throws<ArgumentException>(() => product.Name = " ");
        Assert.Throws<ArgumentOutOfRangeException>(() => product.Price = -1m);
    }

    [Fact]
    public void ExtensionBlocks_PropertyAndConstrainedMethod_Work()
    {
        string?[] names = ["a", null, "b"];

        Assert.Equal(new[] { "a", "b" }, names.WhereNotNull());
        Assert.True(Array.Empty<int>().IsEmpty);
        Assert.False(names.IsEmpty);
    }

    // --- Stories -----------------------------------------------------------------------------------

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_ASingletonKeepsTheDbContext_InProductionTwoRequestsShareIt_UnlessValidationIsOn()
    {
        // Given: a singleton that takes a scoped service, deployed with the Production defaults
        var defaults = Builder(Environments.Production);
        defaults.Services.AddScoped<UnitOfWork>().AddSingleton<ReportCache>();
        using var host = defaults.Build();

        // When: two requests use it
        UnitOfWork first, second;
        using (var request = host.Services.CreateScope())
            first = request.ServiceProvider.GetRequiredService<ReportCache>().Work;
        using (var request = host.Services.CreateScope())
            second = request.ServiceProvider.GetRequiredService<ReportCache>().Work;

        // Then: they share one "scoped" instance; with validation on everywhere, the host refuses to build
        Assert.Same(first, second);

        var validated = Builder(Environments.Production).ValidateServicesInEveryEnvironment();
        validated.Services.AddScoped<UnitOfWork>().AddSingleton<ReportCache>();
        var ex = Assert.Throws<AggregateException>(() => validated.Build());
        Assert.Contains("scoped", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_ValidateOnBuildAlone_DoesNotCatchACaptiveDependency()
    {
        var builder = Builder(Environments.Production);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true }));
        builder.Services.AddScoped<UnitOfWork>().AddSingleton<ReportCache>();

        using var host = builder.Build();
        Assert.NotNull(host.Services.GetRequiredService<ReportCache>());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheShippingAddressIsMissingFromConfig_TheServiceRefusesToStart()
    {
        // Given: a deploy that lost the Shipping section
        var builder = Builder(Environments.Production).ValidateServicesInEveryEnvironment();
        builder.Services.AddShipping();
        using var host = builder.Build();

        // When: it starts / Then: it stops there, naming the setting
        var ex = await StartFailure(host);
        var failure = Assert.IsType<OptionsValidationException>(ex);
        Assert.Contains("BaseAddress", failure.Message);
    }

    [Theory]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    [InlineData("https://shipping.example.com/api", false, null)]
    [InlineData("https://shipping.example.com/api/", true, "https://shipping.example.com/api/shipments")]
    public async Task Story_TheBaseAddressHasAPath_CallsKeepIt(string baseAddress, bool starts, string? called)
    {
        var handler = new RecordingHandler();
        var builder = Builder(Environments.Production, ("Shipping:BaseAddress", baseAddress)).ValidateServicesInEveryEnvironment();
        builder.Services.AddShipping().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();

        var ex = await StartFailure(host);
        Assert.Equal(starts, ex is null);
        if (!starts)
        {
            Assert.Contains("must end with '/'", Assert.IsType<OptionsValidationException>(ex).Message);
            return;
        }

        var shipment = await host.Services.GetRequiredService<ShippingClient>().CreateAsync(7, CancellationToken.None);
        Assert.Equal("TRK-7", shipment.TrackingNumber);
        Assert.Equal(called, handler.LastRequest!.ToString());
    }

    [Fact]
    public void Uri_LeadingSlashOrMissingTrailingSlash_DropsTheBasePath()
    {
        Assert.Equal("https://shipping.example.com/shipments",
            new Uri(new Uri("https://shipping.example.com/api/"), "/shipments").ToString());
        Assert.Equal("https://shipping.example.com/shipments",
            new Uri(new Uri("https://shipping.example.com/api"), "shipments").ToString());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_SomeoneRemovesTheSmsRegistration_TheServiceFailsAtStartNotAtTheFirstReminder()
    {
        var complete = Builder(Environments.Production).ValidateServicesInEveryEnvironment();
        complete.Services.AddNotifications();
        using (var host = complete.Build())
            Assert.Equal(new[] { "email", "sms" }, host.Services.GetRequiredService<AppointmentReminders>().Channels);

        var broken = Builder(Environments.Production).ValidateServicesInEveryEnvironment();
        broken.Services.AddKeyedSingleton<INotificationSender, EmailSender>("email").AddSingleton<AppointmentReminders>();
        Assert.Throws<AggregateException>(() => broken.Build());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_APatientHoldsASlot_FifteenMinutesLaterItLapses()
    {
        // Given: a patient holds slot 42
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
        var hold = SlotHold.Place(42, time);

        // When / Then: still held a second before fifteen minutes, gone at fifteen
        time.Advance(SlotHold.HoldFor - TimeSpan.FromSeconds(1));
        Assert.False(hold.HasLapsed(time));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(hold.HasLapsed(time));
    }

    // --- Production --------------------------------------------------------------------------------

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoTzdata_AHoldNearMidnightShowsTheNextDayInRiyadh()
    {
        ProductionConditions.Require();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 21, 50, 0, TimeSpan.Zero));

        var hold = SlotHold.Place(42, time);

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 1, 5, 0, TimeSpan.FromHours(3)), hold.ExpiresAtInRiyadh);
        Assert.Equal(TimeSpan.FromHours(3), hold.ExpiresAtInRiyadh.Offset);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_TheValidatedHostStartsAndTheTypedClientCallsTheRightUrl()
    {
        ProductionConditions.Require();
        var handler = new RecordingHandler();
        var builder = Builder(Environments.Production, ("Shipping:BaseAddress", "https://shipping.example.com/api/"))
            .ValidateServicesInEveryEnvironment();
        builder.Services.AddShipping().ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.AddNotifications();
        using var host = builder.Build();

        Assert.Null(await StartFailure(host));
        var shipment = await host.Services.GetRequiredService<ShippingClient>().CreateAsync(7, CancellationToken.None);

        Assert.Equal(new Shipment(7, "TRK-7"), shipment);
        Assert.Equal("https://shipping.example.com/api/shipments", handler.LastRequest!.ToString());
        Assert.Equal(new Uri("https://shipping.example.com/api/"),
            host.Services.GetRequiredService<IOptions<ShippingOptions>>().Value.BaseAddress);
    }
}
