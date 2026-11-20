namespace SkillSamples.Localization;

/// <summary>CLDR's plural categories. Arabic uses all six; English uses One and Other.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>
/// Which plural form a count takes, by CLDR's rules for whole numbers. Arithmetic only, no culture, so it
/// works on an image without ICU.
/// </summary>
public static class Plurals
{
    public static PluralCategory For(string language, long count) => language switch
    {
        LanguageHeader.Arabic => Arabic(count),
        _ => count == 1 ? PluralCategory.One : PluralCategory.Other,
    };

    // لا مواعيد (zero) · موعد واحد (one) · موعدان (two) · 3-10 مواعيد (few) · 11-99 موعدًا (many) ·
    // 100, 101, 102 موعد (other). From 103 on the last two digits decide: 103 is few, 111 is many.
    private static PluralCategory Arabic(long count)
    {
        var n = Math.Abs(count);
        var lastTwo = n % 100;
        return n switch
        {
            0 => PluralCategory.Zero,
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            _ when lastTwo is >= 3 and <= 10 => PluralCategory.Few,
            _ when lastTwo is >= 11 and <= 99 => PluralCategory.Many,
            _ => PluralCategory.Other,
        };
    }
}

public static class PluralTranslations
{
    /// <summary>
    /// The text for a count: the key with the category added (<c>Sms.Appointments.few</c>), then the key's
    /// <c>.other</c>, then English as for any key. The count is filled in invariant, as every placeholder is.
    /// </summary>
    public static async Task<string> GetCountAsync(this TranslationCatalog catalog, string key, string language, long count,
        CancellationToken cancellationToken = default)
    {
        var category = Plurals.For(language, count).ToString().ToLowerInvariant();
        var exact = $"{key}.{category}";
        var text = await catalog.GetAsync(exact, language, cancellationToken);
        if (text == exact) text = await catalog.GetAsync($"{key}.other", language, cancellationToken);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, text, count);
    }
}
