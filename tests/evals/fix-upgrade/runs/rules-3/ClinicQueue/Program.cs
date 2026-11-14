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
builder.Services.AddSingleton<VisitReads>();
builder.Services.AddHostedService<DirectoryWarmer>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();

// Behind the ingress and the service's nginx, both in the pod network.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var network = builder.Configuration["Proxies:Network"]
        ?? throw new InvalidOperationException("Proxies:Network is not configured.");
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    o.ForwardLimit = 2;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "clinic-queue";
        o.MapInboundClaims = false;
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapQueueEndpoints();

app.Run();

public partial class Program;
