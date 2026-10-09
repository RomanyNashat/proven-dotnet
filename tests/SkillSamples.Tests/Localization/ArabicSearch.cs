using System.Text;

namespace SkillSamples.Localization;

public static class ArabicSearch
{
    public static string NormalizeForSearch(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ') continue;   // tashkeel, tatweel
            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => ch,
            });
        }
        return builder.ToString().Trim();
    }
}
