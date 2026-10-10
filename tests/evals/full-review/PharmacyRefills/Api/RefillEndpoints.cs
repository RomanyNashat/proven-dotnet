using Microsoft.AspNetCore.Authorization;
using PharmacyRefills.Application;
using PharmacyRefills.Infrastructure;

namespace PharmacyRefills.Api;

public sealed record RequestRefill(int PatientId, string NationalId, string Medication);

public static class RefillEndpoints
{
    public static void MapRefills(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/refills").RequireAuthorization();

        group.MapPost("/", async (RequestRefill body, RefillService service, CancellationToken ct) =>
            TypedResults.Created($"/refills/{await service.RequestAsync(body.PatientId, body.NationalId, body.Medication, ct)}"));

        group.MapGet("/{id:int}", async (int id, RefillQueries queries, CancellationToken ct) =>
            await queries.FindAsync(id, ct) is { } refill ? Results.Ok(refill) : Results.NotFound());

        group.MapGet("/search", async (string medication, RefillQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.SearchAsync(medication, ct)));

        group.MapPost("/{id:int}/dispense", async (int id, string? notes, RefillService service, CancellationToken ct) =>
        {
            await service.DispenseAsync(id, notes, ct);
            return TypedResults.NoContent();
        });

        app.MapGet("/health", [AllowAnonymous] () => TypedResults.Ok("healthy"));
    }
}
