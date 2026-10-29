using System.Security.Claims;
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
builder.Services.AddHttpClient<LabClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Labs:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Sms:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "lab-results";
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "lab-results",
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role,
        };
        // Keycloak nests realm roles in the "realm_access" object claim; flatten them into role claims.
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                KeycloakRoles.Flatten(ctx.Principal);
                return Task.CompletedTask;
            },
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
