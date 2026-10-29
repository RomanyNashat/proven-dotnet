using FluentValidation;
using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Lifetimes are checked in every environment, not only Development: a captive scoped service then stops
// the deploy at start-up instead of sharing one instance across requests (dotnet-core).
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddLabDb();
builder.Services.AddDownstreamClients();
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddTransient<IValidator<RecordResultRequest>, RecordResultRequestValidator>();
builder.Services.AddTransient<IValidator<LookupRequest>, LookupRequestValidator>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});
// Only proxies on loopback are trusted by default; add the ingress network (KnownNetworks) per deployment.
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddKeycloakJwt(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    // A token without sub can't be audited, so it records and reads nothing.
    .AddPolicy(ResultEndpoints.ClinicianPolicy, p => p
        .RequireRole("clinician")
        .RequireAssertion(ctx => CurrentUser.IdOf(ctx.User) is not null));
builder.Services.AddPerCallerRateLimits(builder.Configuration);

var app = builder.Build();

app.UseApiSecurityHeaders();
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
