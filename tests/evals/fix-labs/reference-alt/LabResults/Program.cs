using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// HttpClient logs every request URI at Information, and the lab's URIs carry the national ID.
builder.Logging.AddFilter("System.Net.Http.HttpClient.LabClient", LogLevel.Warning);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddPooledDbContextFactory<LabDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Lab"))
    .AddInterceptors(new AuditInterceptor()));
builder.Services.AddScoped(sp =>
{
    var db = sp.GetRequiredService<IDbContextFactory<LabDbContext>>().CreateDbContext();
    db.CallerId = sp.GetRequiredService<CurrentUser>().Id;
    return db;
});

builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHttpClient<LabClient>(http => http.BaseAddress = new Uri(builder.Configuration["Labs:BaseAddress"]!));
builder.Services.AddHttpClient<SmsClient>(http => http.BaseAddress = new Uri(builder.Configuration["Sms:BaseAddress"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "lab-results";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "lab-results",
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(15),
        };
    });
builder.Services.AddTransient<IClaimsTransformation, RealmRoles>();
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
