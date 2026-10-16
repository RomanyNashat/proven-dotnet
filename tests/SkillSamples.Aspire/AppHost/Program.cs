// AppHost/Program.cs — defines the distributed application
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure: containers locally, with data that survives a restart
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("postgres-data");
var orderDb = postgres.AddDatabase("orderdb");

// SQL Server instead (Aspire.Hosting.SqlServer):
// var sql = builder.AddSqlServer("sql").WithDataVolume("sql-data");
// var orderDb = sql.AddDatabase("orderdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume("redis-data");

// Services get their connection strings from WithReference, and start once WaitFor's resources are up
builder.AddProject<Projects.OrderService_Api>("order-service")
    .WithReference(orderDb)
    .WithReference(redis)
    .WaitFor(orderDb)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

builder.Build().Run();
