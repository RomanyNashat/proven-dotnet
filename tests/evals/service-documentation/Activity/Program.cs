using Confluent.Kafka;
using MongoDB.Driver;
using Activity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(builder.Configuration["Mongo:ConnectionString"]));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(builder.Configuration["Mongo:Database"]));
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = builder.Configuration["Kafka:BootstrapServers"], Acks = Acks.All }).Build());
builder.Services.AddOptions<ActivityOptions>().BindConfiguration("Activity").ValidateOnStart();
builder.Services.AddScoped<ActivityService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var steps = app.MapGroup("/api/steps").RequireAuthorization();
steps.MapGet("/today", ActivityEndpoints.Today);
steps.MapPost("/", ActivityEndpoints.Sync);
steps.MapGet("/history", ActivityEndpoints.History);

app.Run();
