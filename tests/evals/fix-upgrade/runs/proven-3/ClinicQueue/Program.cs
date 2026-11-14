using ClinicQueue.Api;
using ClinicQueue.Application;
using ClinicQueue.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Production skips these checks by default; a captive dependency should fail at start-up there too.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// One data source, so EF and the raw reads share one pool (EFCore.PG 9+ builds its own from a bare connection string).
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Queue")
        ?? throw new InvalidOperationException("ConnectionStrings:Queue is not set.")));
builder.Services.AddDbContext<QueueDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<ClinicDirectory>();
builder.Services.AddScoped<IClinicSource, EfClinicSource>();
builder.Services.AddScoped<QueueSnapshots>();
builder.Services.AddScoped<VisitBookings>();
builder.Services.AddSingleton<VisitReads>();
builder.Services.AddHostedService<DirectoryWarmer>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();

// Behind the ingress and the service's nginx, both in the pod network.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var network = builder.Configuration["Proxies:Network"]
        ?? throw new InvalidOperationException("Proxies:Network is not set: without it the client IP and scheme are nginx's.");
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
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
    app.UseHsts();
}

app.Use((http, next) =>
{
    http.Response.Headers.XContentTypeOptions = "nosniff";
    return next(http);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapQueueEndpoints();

app.Run();

public partial class Program;
