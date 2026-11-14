using ClinicQueue.Api;
using ClinicQueue.Application;
using ClinicQueue.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QueueDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Queue")));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<ClinicDirectory>();
builder.Services.AddScoped<IClinicSource, EfClinicSource>();
builder.Services.AddScoped<QueueSnapshots>();
builder.Services.AddScoped<VisitBooking>();
builder.Services.AddSingleton<VisitReads>();
builder.Services.AddHostedService<DirectoryWarmer>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();

// Behind the ingress and the service's nginx, both in the pod network.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(builder.Configuration["Proxies:Network"]
        ?? throw new InvalidOperationException("Proxies:Network is not configured.")));
    o.ForwardLimit = 2;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "clinic-queue";
        o.MapInboundClaims = false;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    // Only sent on HTTPS requests, which behind the ingress means X-Forwarded-Proto: https.
    app.UseHsts();
}

app.Use((http, next) =>
{
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    return next(http);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapQueueEndpoints();

app.Run();

public partial class Program;
