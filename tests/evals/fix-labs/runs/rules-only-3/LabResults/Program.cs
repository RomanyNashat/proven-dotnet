using FluentValidation;
using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

const string Audience = "lab-results";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

// The clinician is passed to the entity explicitly; a request-scoped interceptor inside a pooled context
// was resolved once from the root provider, so every result was stamped with the same (wrong) user.
builder.Services.AddDbContextPool<LabDbContext>(
    o => o.UseNpgsql(builder.Configuration.GetConnectionString("Lab")),
    poolSize: 128);

builder.Services.AddTransient<IValidator<RecordResultRequest>, RecordResultRequestValidator>();
builder.Services.AddTransient<IValidator<LookupRequest>, LookupRequestValidator>();

builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();
AddServiceEndpoint(builder.Services, "Labs");
AddServiceEndpoint(builder.Services, "Sms");

// The lab URL carries the national ID in its path, and the default HttpClient logging writes every
// request URL at Information level.
builder.Services.AddHttpClient<LabClient>(ConfigureFrom("Labs")).RemoveAllLoggers();
builder.Services.AddHttpClient<SmsClient>(ConfigureFrom("Sms"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = Audience;

        // Keep Keycloak's claim names ("sub", "realm_access") instead of the WS-* mapping.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = Audience,
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
                    KeycloakRoles.AddRoles(identity, Audience);
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireRole("clinician"));

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    headers.CacheControl = "no-store";
    return next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

static void AddServiceEndpoint(IServiceCollection services, string section) =>
    services.AddOptions<ServiceEndpointOptions>(section)
        .BindConfiguration(section)
        .ValidateDataAnnotations()
        .ValidateOnStart();

static Action<IServiceProvider, HttpClient> ConfigureFrom(string section) => (sp, http) =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<ServiceEndpointOptions>>().Get(section);
    http.BaseAddress = options.BaseAddress;
    http.Timeout = options.Timeout;
};

public partial class Program;
