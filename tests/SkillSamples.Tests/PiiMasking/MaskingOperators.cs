using System.Text.RegularExpressions;
using Serilog.Enrichers.Sensitive;

namespace SkillSamples.PiiMasking;

// Saudi national ID or iqama: 10 digits starting with 1 or 2. Keep the last four only: the first digit
// says citizen or resident, which is itself personal data. Also matches 10-digit epoch seconds; masking
// those too is the safe side of the trade.
public sealed class SaudiNationalIdMaskingOperator() : RegexMaskingOperator(@"(?<!\d)[12]\d{9}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "******" + match.Value[^4..];
}

// Saudi mobile: 05XXXXXXXX, 5XXXXXXXX, +9665XXXXXXXX or 009665XXXXXXXX.
public sealed class SaudiMobileMaskingOperator() : RegexMaskingOperator(@"(?<!\d)(?:\+966|00966|0)?5\d{8}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "*******" + match.Value[^2..];
}

public sealed class MedicalRecordNumberMaskingOperator()
    : RegexMaskingOperator(@"MRN[-:]?\s*\d{6,12}", RegexOptions.Compiled | RegexOptions.IgnoreCase)
{
    protected override string PreprocessMask(string mask, Match match) => "MRN:" + Masking.Full;
}
