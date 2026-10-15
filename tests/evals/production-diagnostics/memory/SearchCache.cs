// Excerpt the team pointed at: added in the 2026-10-13 release to speed up patient search.
public sealed class SearchCache
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<PatientHit>> Results = new();

    public async Task<IReadOnlyList<PatientHit>> SearchAsync(string query, Func<string, Task<IReadOnlyList<PatientHit>>> search)
    {
        if (Results.TryGetValue(query, out var cached))
            return cached;

        var hits = await search(query);
        Results[query] = hits;
        return hits;
    }
}
