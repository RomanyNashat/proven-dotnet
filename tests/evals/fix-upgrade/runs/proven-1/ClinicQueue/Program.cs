using ClinicQueue.Api;
using ClinicQueue.Application;
using ClinicQueue.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Production skips these by default; a captive dependency should stop the start everywhere, not only in Development.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// Settings are read when the services are built, not here, so configuration added by a host or a test applies.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Queue")
    ?? throw new InvalidOperationException("ConnectionStrings:Queue is not set.")));
builder.Services.AddDbContext<QueueDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ClinicDirectory>();
builder.Services.AddScoped<IClinicSource, EfClinicSource>();
builder.Services.AddScoped<QueueSnapshots>();
builder.Services.AddScoped<VisitBookings>();
builder.Services.AddSingleton<QueueSnapshotCache>();
builder.Services.AddSingleton<VisitReads>();
builder.Services.AddHostedService<DirectoryWarmer>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();

// Behind the ingress and the service's nginx, both in the pod network. KnownNetworks and its IPNetwork are
// obsolete on .NET 10; KnownIPNetworks takes System.Net.IPNetwork.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, configuration) =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var network = configuration["Proxies:Network"] ?? throw new InvalidOperationException("Proxies:Network is not set.");
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    o.ForwardLimit = 2;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IConfiguration>((o, configuration) =>
{
    o.Authority = configuration["Auth:Authority"];
    o.Audience = "clinic-queue";
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
})
    // Without an authority the pod would start, pass its liveness probe and answer 401 to everything.
    .Validate(o => Uri.TryCreate(o.Authority, UriKind.Absolute, out _), "Auth:Authority must be an absolute URL.")
    .ValidateOnStart();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.Use((http, next) =>
{
    // A JSON-only API: nothing here should be sniffed, framed or run as a page.
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    return next(http);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapQueueEndpoints();

app.Run();

public partial class Program;
