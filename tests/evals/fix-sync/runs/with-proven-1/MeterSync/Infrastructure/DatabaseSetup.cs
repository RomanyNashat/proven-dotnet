using MeterSync.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Infrastructure;

public static class DatabaseSetup
{
    // Npgsql's default is 100 per pod, and PostgreSQL's max_connections defaults to 100 in total. Several pods
    // at 20 fit; agree the number with the DBA. A pool size in the connection string itself wins.
    public const int DefaultMaxPoolSize = 20;

    public static string WithPoolSize(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
        {
            builder.MaxPoolSize = DefaultMaxPoolSize;
        }

        return builder.ConnectionString;
    }

    public static IServiceCollection AddSyncDatabase(this IServiceCollection services)
    {
        // Read when the first context is built, so configuration added by the host (or a test) is seen.
        services.AddDbContext<SyncDbContext>((sp, o) => o.UseNpgsql(
            WithPoolSize(sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync")
                ?? throw new InvalidOperationException("ConnectionStrings:Sync is not set.")),
            npgsql => npgsql.EnableRetryOnFailure()));

        services.AddScoped<PatientDirectory>();
        services.AddScoped<IPatientMeters>(sp => sp.GetRequiredService<PatientDirectory>());
        services.AddScoped<IMeterLinks, EfMeterLinks>();
        services.AddScoped<ISyncStore, EfSyncStore>();
        services.AddScoped<IAlertStore, EfAlertStore>();
        return services;
    }
}
