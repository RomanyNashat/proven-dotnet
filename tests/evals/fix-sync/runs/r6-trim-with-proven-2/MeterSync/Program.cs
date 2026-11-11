using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);

// Fail at start-up on a captive dependency instead of sharing one DbContext across requests.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = WithTrailingSlash(sp.GetRequiredService<IConfiguration>()["MeterVendor:BaseAddress"]))
    .AddStandardResilienceHandler();

// No retry: the gateway has no idempotency key, so a retried POST is a second text to the patient.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Sms:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = Audience;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser with a bearer token; no cookies, so no credentials.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type")));

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// OnStarting, and this early, so 401s, preflights and error responses carry the headers too.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    return next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

// Relative paths replace the base's last segment unless it ends in '/', which would drop "/v2" from every call.
static Uri WithTrailingSlash(string? baseAddress)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);
    return new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
}

public partial class Program;
