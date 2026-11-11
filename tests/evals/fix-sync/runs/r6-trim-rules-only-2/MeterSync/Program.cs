using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Settings are read from IConfiguration when the services are built, not eagerly here, so configuration
// added by the host (or a test factory) after this line is still honoured.
builder.Services.AddDbContext<SyncDbContext>((sp, o) => o.UseNpgsql(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync")
    ?? throw new InvalidOperationException("ConnectionStrings:Sync is required.")));
builder.Services.AddSingleton(TimeProvider.System);

// Scoped: these use the scoped DbContext. As singletons they captured one DbContext for every request.
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterLinker>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = BaseAddress.From(sp.GetRequiredService<IConfiguration>()["MeterVendor:BaseAddress"], "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retry handler on purpose: the gateway has no idempotency key, so a retried POST can text twice.
// The sweeper retries a failed alert itself, under its claim.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = BaseAddress.From(sp.GetRequiredService<IConfiguration>()["Sms:BaseAddress"], "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = "meter-sync";
        o.RequireHttpsMetadata = true;
        o.TokenValidationParameters.ValidateAudience = true;
        o.TokenValidationParameters.ValidAudience = "meter-sync";
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser. Only its origins; the API takes bearer tokens in
// a header, so no cookies (credentials) are allowed cross-origin.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((o, config) => o.AddDefaultPolicy(p => p
        .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .WithHeaders("Authorization", "Content-Type")
        .WithMethods("GET", "POST")));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
