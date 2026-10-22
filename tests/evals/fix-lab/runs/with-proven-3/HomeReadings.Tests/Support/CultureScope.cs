using System.Globalization;

namespace HomeReadings.Tests.Support;

/// <summary>Runs code under a given culture and puts the previous one back.</summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// A culture whose decimal separator is the Arabic one, as ar-SA has with some ICU versions. Built from
    /// the invariant culture so it exists on a machine without ICU too.
    /// </summary>
    public static CultureInfo ArabicSeparators()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = "٫";
        culture.NumberFormat.NumberGroupSeparator = "٬";
        return culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
