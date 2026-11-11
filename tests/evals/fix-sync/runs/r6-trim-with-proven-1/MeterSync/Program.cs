using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Catch captive dependencies (a scoped service held by a singleton) at start-up in every environment,
// not only in Development.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterLinker>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = BaseAddress.WithTrailingSlash(builder.Configuration["MeterVendor:BaseAddress"]))
    .AddStandardResilienceHandler();

// No retry handler: the gateway has no idempotency key, so every POST is a text to the patient.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = BaseAddress.WithTrailingSlash(builder.Configuration["Sms:BaseAddress"]);
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "meter-sync";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "meter-sync",
            ValidateAudience = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser with a bearer header; no cookies, so no credentials.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, config) =>
    o.AddDefaultPolicy(p => p
        .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .WithMethods(HttpMethods.Get, HttpMethods.Post)
        .WithHeaders("Authorization", "Content-Type")));

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseStatusCodePages();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
