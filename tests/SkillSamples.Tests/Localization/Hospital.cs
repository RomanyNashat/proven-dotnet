namespace SkillSamples.Localization;

public sealed class Hospital
{
    public Hospital(string nameAr, string nameEn) => Rename(nameAr, nameEn);

    private Hospital()
    {
    }

    public int Id { get; private set; }

    public string NameAr { get; private set; } = "";

    public string NameEn { get; private set; } = "";

    /// <summary>Derived from NameAr on every write, so it can't drift from the name.</summary>
    public string NameArSearch { get; private set; } = "";

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        NameArSearch = ArabicSearch.NormalizeForSearch(nameAr);
    }
}
