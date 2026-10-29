using System.Security.Claims;
using System.Text.Json;
using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<LabDbContext>((sp, o) => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Lab"))
    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();
// The default HttpClient logging writes full request URLs, which contain the patient's national id.
builder.Services.AddHttpClient<LabClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Labs:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllLoggers();
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Sms:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllLoggers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "lab-results";
        o.MapInboundClaims = false;
        o.Events = new JwtBearerEvents
        {
            // Keycloak puts roles in a nested claim: "realm_access": { "roles": [...] }.
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity
                    && context.Principal.FindFirst("realm_access")?.Value is { } realmAccess)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(realmAccess);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object
                            && doc.RootElement.TryGetProperty("roles", out var roles)
                            && roles.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var role in roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                return Task.CompletedTask;
            },
        };
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "lab-results",
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireRole("clinician"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
