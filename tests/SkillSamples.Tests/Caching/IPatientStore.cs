namespace SkillSamples.Caching;

public interface IPatientStore
{
    Task<string> LoadNameAsync(int id, CancellationToken ct);
}
