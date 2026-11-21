using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
// Both read the user from the current request at call time, so they are safe as singletons and with a pooled
// DbContext. (Previously a scoped interceptor was resolved once from the root provider by the pool, freezing
// whichever user - or "system" - it saw first into every later result's RecordedBy.)
builder.Services.AddSingleton<CurrentUser>();
builder.Services.AddSingleton<AuditInterceptor>();
builder.Services.AddDbContextPool<LabDbContext>((sp, o) => o
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
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "lab-results";
        // Keep Keycloak's claim names ("sub", "realm_access", ...) instead of mapping them to WS-* URIs.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = "lab-results",
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
        };
    });
// Keycloak nests roles in JSON (realm_access.roles); there is no claim literally named "realm_access.roles", so
// without this every clinician was refused.
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p
        .RequireAuthenticatedUser()
        .RequireRole("clinician")
        // RecordedBy is the clinician's sub: refuse tokens that don't carry one.
        .RequireAssertion(c => CurrentUser.SubjectOf(c.User) is not null));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
