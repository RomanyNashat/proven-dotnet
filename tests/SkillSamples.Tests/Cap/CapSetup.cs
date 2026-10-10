using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SkillSamples.Cap;

public static class CapSetup
{
    public static IServiceCollection AddEventBus(this IServiceCollection services, string postgres, Uri rabbitMq, string serviceName)
    {
        services.AddCap(x =>
        {
            x.UsePostgreSql(o => { o.ConnectionString = postgres; o.Schema = "cap"; });
            x.UseRabbitMQ(o =>
            {
                o.HostName = rabbitMq.Host;
                o.Port = rabbitMq.Port;
                (o.UserName, o.Password) = (rabbitMq.UserInfo.Split(':')[0], rabbitMq.UserInfo.Split(':')[1]);
                o.PublishConfirms = true;   // off by default: a message CAP marked as sent could be lost by the broker
            });
            x.DefaultGroupName = serviceName;    // the queue this service's subscribers read
            x.FailedRetryCount = 5;              // the default, 50, retries a broken message for most of an hour
        });

        // CAP creates its schema at every start (CREATE SCHEMA/TABLE IF NOT EXISTS), which needs DDL rights.
        // The schema comes from a reviewed script instead; at start-up the service only checks it's there.
        services.AddSingleton<IStorageInitializer, ReviewedSchemaCheck>();
        return services;
    }
}

public sealed class ReviewedSchemaCheck(IOptions<PostgreSqlOptions> options) : IStorageInitializer
{
    public string GetPublishedTableName() => $"\"{options.Value.Schema}\".\"published\"";
    public string GetReceivedTableName() => $"\"{options.Value.Schema}\".\"received\"";
    public string GetLockTableName() => $"\"{options.Value.Schema}\".\"lock\"";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var check = new NpgsqlCommand(
            "SELECT to_regclass(@published) IS NOT NULL AND to_regclass(@received) IS NOT NULL", connection);
        check.Parameters.AddWithValue("published", GetPublishedTableName());
        check.Parameters.AddWithValue("received", GetReceivedTableName());
        if (await check.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("CAP's tables are missing: apply the reviewed CAP schema script first.");
    }
}
