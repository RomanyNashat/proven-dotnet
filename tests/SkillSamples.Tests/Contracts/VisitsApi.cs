using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SkillSamples.Contracts;

/// <summary>The same visits API at two releases, each serving its OpenAPI document.</summary>
public static class VisitsApi
{
    public static readonly DateTimeOffset DeprecatedSince = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Sunset = new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

    public static Task<WebApplication> StartV1Async() => StartAsync(app =>
    {
        app.MapGet("/visits/{id:int}", (int id) => TypedResults.Ok(new V1.Visit(id, V1.VisitStatus.Booked, "Dr Sara")));
        app.MapPost("/visits", (V1.NewVisit visit) => TypedResults.Created($"/visits/{visit.PatientId}"));
    });

    public static Task<WebApplication> StartV2Async(bool newRequiredField = false) => StartAsync(app =>
    {
        app.MapGet("/visits/{id:int}", (int id) => TypedResults.Ok(new V2.Visit(id, id == 2 ? V2.VisitStatus.Cancelled : V2.VisitStatus.Booked, "Dr Sara", "B12")))
            .Deprecated(DeprecatedSince, Sunset, "/v2/visits/{id}");
        if (newRequiredField)
        {
            app.MapPost("/visits", (NewVisitWithReason visit) => TypedResults.Created($"/visits/{visit.PatientId}"));
        }
        else
        {
            app.MapPost("/visits", (V2.NewVisit visit) => TypedResults.Created($"/visits/{visit.PatientId}"));
        }
    });

    // A release that makes "reason" required: every installed app's request now lacks it.
    public sealed record NewVisitWithReason(int PatientId, string? Note, string Reason);

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.RespectRequiredConstructorParameters = true;   // a missing required field is a 400
        });
        builder.Services.AddOpenApi();
        var app = builder.Build();
        app.MapOpenApi();
        map(app);
        await app.StartAsync();
        return app;
    }
}
