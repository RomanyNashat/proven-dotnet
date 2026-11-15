namespace Coverage;

public sealed class LabResults(ILabStore labs)
{
    public async Task<decimal> LatestHbA1cAsync(int patientId, CancellationToken ct)
    {
        var results = await labs.ForPatientAsync(patientId, "HbA1c", ct);
        return results.OrderByDescending(r => r.TakenAt).FirstOrDefault()?.Value ?? 0;
    }

    public int AgeFrom(string raw)
    {
        int.TryParse(raw, out var age);
        return age;
    }

    public int AgeOrUnknown(string raw) => int.TryParse(raw, out var age) ? age : -1;

    public string Label(Lab? lab) => lab?.Name ?? "(unnamed)";
}
