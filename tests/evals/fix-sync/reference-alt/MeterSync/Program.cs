using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<SyncDbContext>>().CreateDbContext());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = new Uri(builder.Configuration["MeterVendor:BaseAddress"]!))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<SmsClient>(http =>
    http.BaseAddress = new Uri(builder.Configuration["Sms:BaseAddress"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "meter-sync";
        o.TokenValidationParameters = new TokenValidationParameters { ClockSkew = TimeSpan.FromSeconds(30) };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser.
builder.Services.AddCors(o => o.AddPolicy("clinic", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

var app = builder.Build();

app.UseCors("clinic");
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
