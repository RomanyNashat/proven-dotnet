using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PharmacyRefills.Domain;
using PharmacyRefills.Infrastructure;

namespace PharmacyRefills.Application;

public sealed class RefillService(RefillsDbContext db, RefillQueries queries, TimeProvider time, ILogger<RefillService> logger)
{
    public async Task<int> RequestAsync(int patientId, string nationalId, string medication, CancellationToken ct)
    {
        logger.LogInformation("Refill requested by {NationalId} for {Medication}", nationalId, medication);

        var refill = Refill.Request(patientId, medication, time.GetUtcNow());
        db.Refills.Add(refill);
        await db.SaveChangesAsync(ct);

        NotifyInsurer(refill.Id);
        return refill.Id;
    }

    public async Task DispenseAsync(int id, string? notes, CancellationToken ct)
    {
        var refill = await queries.FindAsync(id, ct) ?? throw new KeyNotFoundException($"Refill {id}");
        refill.Dispense(notes);
        await db.SaveChangesAsync(ct);
    }

    private void NotifyInsurer(int refillId)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("https://insurer.example.com/") };
            var response = http.PostAsJsonAsync("refills", new { refillId }).Result;
            response.EnsureSuccessStatusCode();
        }
        catch (Exception)
        {
        }
    }
}
