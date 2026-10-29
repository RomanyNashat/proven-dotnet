using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<RequiredSettings>()
    .Configure<IConfiguration>(RequiredSettings.Bind)
    .Validate(s => s.IsValid(), "ConnectionStrings:Lab, Labs:BaseAddress, Sms:BaseAddress and Auth:Authority are required.")
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditInterceptor>();

// Not pooled: the audit interceptor depends on the request's user, and a pooled context would resolve it once, from the root.
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
        o.TokenValidationParameters.ValidAudience = "lab-results";
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireRole("clinician"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
