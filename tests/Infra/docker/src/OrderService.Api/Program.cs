// The smallest service the docker-dotnet Dockerfile and the k6 script can run against.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health/live", () => "Healthy");
app.MapGet("/api/patients/{id:int}", (int id) => new { id });

// The same endpoint, too slow: k6 with BASE_URL=.../slow must fail its p(95) < 500 ms threshold.
app.MapGet("/slow/api/patients/{id:int}", async (int id) =>
{
    await Task.Delay(600);
    return new { id };
});

app.Run();
