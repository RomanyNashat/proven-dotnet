using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyRefills.Api;
using PharmacyRefills.Application;
using PharmacyRefills.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Refills")!;

builder.Services.AddDbContext<RefillsDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RefillQueries>();
builder.Services.AddScoped<RefillService>();
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapRefills();
app.Run();
