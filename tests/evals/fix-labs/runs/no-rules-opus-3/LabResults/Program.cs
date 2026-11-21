using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();

// Singleton: it reads the signed-in user at save time, so it is safe with a pooled DbContext.
builder.Services.AddSingleton<AuditInterceptor>();
builder.Services.AddDbContextPool<LabDbContext>((sp, o) => o
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Lab"))
    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();

// The default HttpClient logging writes request URIs at Information level; the lab URI contains the national ID.
builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());
builder.Services.AddHttpClient<LabClient>((sp, http) =>
{
    http.BaseAddress = BaseAddress(sp, "Labs:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = BaseAddress(sp, "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) => KeycloakJwt.Configure(o, config["Auth:Authority"], KeycloakJwt.Audience));
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireAuthenticatedUser().RequireClaim("sub").RequireRole("clinician"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

static Uri BaseAddress(IServiceProvider sp, string key)
{
    var value = sp.GetRequiredService<IConfiguration>()[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new OptionsValidationException(key, typeof(Uri), [$"{key} is not configured."]);
    }

    // Relative request paths ("patients/...") would drop the last segment of a base address without a trailing slash.
    return new Uri(value.EndsWith('/') ? value : value + "/", UriKind.Absolute);
}

public partial class Program;
