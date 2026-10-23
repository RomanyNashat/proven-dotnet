using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);

// Everything that holds a SyncDbContext is scoped: a singleton would share one DbContext across
// concurrent requests (DbContext isn't thread-safe) and keep it for the life of the pod.
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = HttpBaseAddress.From(sp.GetRequiredService<IConfiguration>(), "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retry handler on the SMS client: every POST sends a text and the gateway has no idempotency key,
// so retrying a request whose response was lost would text the patient twice. A failed send is retried
// by the next AlertSweeper run instead.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = HttpBaseAddress.From(sp.GetRequiredService<IConfiguration>(), "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
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

// The clinic dashboard calls this API from the browser, with a bearer token (no cookies), so only its
// origins are allowed and credentials aren't.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(CorsOrigins.From(builder.Configuration))
    .WithMethods(HttpMethods.Get, HttpMethods.Post)
    .AllowAnyHeader()));

var app = builder.Build();

// Fail at start-up, not on the first sync, when a base address is missing or malformed.
HttpBaseAddress.From(app.Configuration, "MeterVendor:BaseAddress");
HttpBaseAddress.From(app.Configuration, "Sms:BaseAddress");

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use((context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    return next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
