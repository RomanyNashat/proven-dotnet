using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

// No interceptor that reads the current user: pooled contexts are shared across requests, so a scoped service
// captured in their options would stamp every result with whoever made the first request.
builder.Services.AddDbContextPool<LabDbContext>(
    o => o.UseNpgsql(builder.Configuration.GetConnectionString("Lab")),
    poolSize: 128);

builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();

// The lab's URLs carry the national ID, so the request log lines (which include the URL) are switched off.
builder.Services.AddHttpClient<LabClient>(http =>
    {
        http.BaseAddress = new Uri(builder.Configuration["Labs:BaseAddress"]!);
        http.Timeout = TimeSpan.FromSeconds(10);
    })
    .RemoveAllLoggers();
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
        o.RequireHttpsMetadata = true;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "lab-results",
            NameClaimType = "preferred_username",
            RoleClaimType = KeycloakRoles.RoleClaimType,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    KeycloakRoles.AddRealmRoles(identity);
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthPolicies.Clinician, p => p.RequireRole("clinician").RequireClaim(AuthPolicies.SubjectClaim));

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    return next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
