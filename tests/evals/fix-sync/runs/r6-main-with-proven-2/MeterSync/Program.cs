using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Catch captive dependencies and missing registrations in every environment, not only Development.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// Configuration is read lazily (from the service provider) so a host's overrides apply.
builder.Services.AddDbContext<SyncDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

foreach (var section in new[] { HttpServiceOptions.MeterVendor, HttpServiceOptions.Sms })
{
    builder.Services.AddOptions<HttpServiceOptions>(section)
        .BindConfiguration(section)
        .Validate(o => o.IsValid, $"{section}:BaseAddress must be an absolute URI.")
        .ValidateOnStart();
}

builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = BaseAddressOf(sp, HttpServiceOptions.MeterVendor))
    .AddStandardResilienceHandler();

// No retry handler: every POST sends an SMS and the gateway has no idempotency key.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = BaseAddressOf(sp, HttpServiceOptions.Sms);
    http.Timeout = SmsClient.Timeout;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = "meter-sync";
        o.TokenValidationParameters.ValidAudience = "meter-sync";
        o.TokenValidationParameters.ValidateAudience = true;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser with a bearer token (no cookies).
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((o, config) => o.AddDefaultPolicy(p => p
        .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type")));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        return Task.CompletedTask;
    });
    await next();
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

static Uri BaseAddressOf(IServiceProvider sp, string section) =>
    BaseAddress.WithTrailingSlash(sp.GetRequiredService<IOptionsMonitor<HttpServiceOptions>>().Get(section).BaseAddress!);

public partial class Program;
