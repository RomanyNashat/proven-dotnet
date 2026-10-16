using Microsoft.Extensions.Caching.Hybrid;

namespace SkillSamples.Caching;

public sealed class PatientNames(HybridCache cache, IPatientStore store)
{
    public static string Key(int id) => $"patient:v1:{id}";   // prefixed and versioned: a new shape gets a new key

    // L1, then L2, then the store, with one store call however many requests arrive for a cold key.
    public ValueTask<string> NameAsync(int id, CancellationToken ct) =>
        cache.GetOrCreateAsync(Key(id), async token => await store.LoadNameAsync(id, token), tags: ["patient"], cancellationToken: ct);
}
