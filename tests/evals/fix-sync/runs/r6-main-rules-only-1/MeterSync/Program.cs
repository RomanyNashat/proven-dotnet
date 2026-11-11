using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Configuration is read inside the option callbacks, not here: test hosts and deployment overrides are
// layered onto it after this code runs.
string Required(string key) =>
    config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(Required("ConnectionStrings:Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterLinker>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>(http => http.BaseAddress = HttpBaseAddress.From(Required("MeterVendor:BaseAddress")))
    .AddStandardResilienceHandler();

// No retry handler: the gateway has no idempotency key, so a retried POST is a second SMS to the patient.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = HttpBaseAddress.From(Required("Sms:BaseAddress"));
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = Required("Auth:Authority");
        o.Audience = Audience;
        o.RequireHttpsMetadata = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser. Bearer tokens travel in a header, so no credentials.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

var app = builder.Build();

// Fail at startup, not on the first request or the first sync run.
_ = Required("ConnectionStrings:Sync");
_ = Required("Auth:Authority");
_ = HttpBaseAddress.From(Required("MeterVendor:BaseAddress"));
_ = HttpBaseAddress.From(Required("Sms:BaseAddress"));

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    return next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
