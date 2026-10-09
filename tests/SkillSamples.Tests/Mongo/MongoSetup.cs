using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace SkillSamples.Mongo;

public static class MongoSetup
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration config, string applicationName, string database)
    {
        MongoConventions.Register();
        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(
                config.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is missing."));
            settings.ApplicationName = applicationName;
            settings.MaxConnectionPoolSize = 50;   // × pods: agree the fleet total with the DBA
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

            // Command name and duration only. e.Command and e.Reply hold whole documents: patient data.
            settings.ClusterConfigurator = cluster => cluster
                .Subscribe<CommandSucceededEvent>(e => MongoMetrics.Record(e.CommandName, e.Duration, failed: false))
                .Subscribe<CommandFailedEvent>(e => MongoMetrics.Record(e.CommandName, e.Duration, failed: true));

            return new MongoClient(settings);   // one per application: it owns the connection pool
        });
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(database));
        return services;
    }
}

public static class MongoConventions
{
    private static int _registered;

    // Once per process, before any collection is used: a type's class map is built the first time it's
    // serialized, and a convention registered after that doesn't change it.
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        ConventionRegistry.Register("house", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),            // documents with a removed field still load
            new EnumRepresentationConvention(BsonType.String),
        }, _ => true);

        // Driver 3.x has no default Guid format and refuses to write a Guid until one is set.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}

public static class MongoMetrics
{
    public const string MeterName = "Samples.MongoDB";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("mongodb.command.duration", unit: "ms");

    public static void Record(string command, TimeSpan duration, bool failed) =>
        Duration.Record(duration.TotalMilliseconds, new("command", command), new("failed", failed));
}
