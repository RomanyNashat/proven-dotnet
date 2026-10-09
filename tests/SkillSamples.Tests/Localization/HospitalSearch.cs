using Microsoft.EntityFrameworkCore;

namespace SkillSamples.Localization;

public sealed record HospitalItem(int Id, string Name);

public static class HospitalSearch
{
    /// <summary>
    /// Matches what the user typed against the normalized column, and returns the name in the request's
    /// language only: the CASE runs in SQL, so the other language never leaves the database.
    /// </summary>
    public static Task<List<HospitalItem>> SearchAsync(
        this HospitalsDbContext db, string language, string typed, CancellationToken ct)
    {
        var term = ArabicSearch.NormalizeForSearch(typed);
        return db.Hospitals.AsNoTracking()
            .Where(h => h.NameArSearch.StartsWith(term))
            .OrderBy(h => h.Id)
            .Select(h => new HospitalItem(h.Id, language == LanguageHeader.Arabic ? h.NameAr : h.NameEn))
            .ToListAsync(ct);
    }
}
