using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Settings are read from the service provider when first needed (not eagerly from builder.Configuration),
// so configuration supplied by the host or a test factory is honoured, and a missing one fails loudly.
static string Required(IConfiguration config, string key) =>
    config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"The setting '{key}' is required.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

// The interceptor is shared by every pooled context, so it (and CurrentUser) read the user at save time.
builder.Services.AddSingleton<CurrentUser>();
builder.Services.AddSingleton<AuditInterceptor>();
builder.Services.AddDbContextPool<LabDbContext>((sp, o) => o
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Lab")
               ?? throw new InvalidOperationException("The connection string 'Lab' is required."))
    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddSingleton<NotificationSignal>();
builder.Services.AddHostedService<NotificationWorker>();

builder.Services.AddHttpClient<LabClient>((sp, http) =>
    {
        http.BaseAddress = new Uri(Required(sp.GetRequiredService<IConfiguration>(), "Labs:BaseAddress"));
        http.Timeout = TimeSpan.FromSeconds(10);
    })
    // The default HttpClient logging writes the request URL, which carries the patient's national ID.
    .RemoveAllLoggers();
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
    {
        http.BaseAddress = new Uri(Required(sp.GetRequiredService<IConfiguration>(), "Sms:BaseAddress"));
        http.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Audience = "lab-results";
        o.TokenValidationParameters.ValidAudience = "lab-results";

        // Keep the JWT claim names as Keycloak sends them: "sub" stays "sub".
        o.MapInboundClaims = false;
        o.TokenValidationParameters.NameClaimType = "preferred_username";
    });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) => o.Authority = Required(config, "Auth:Authority"));

// Keycloak realm roles live in the nested "realm_access.roles" claim; flatten them so RequireRole works.
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireAuthenticatedUser().RequireRole("clinician"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
