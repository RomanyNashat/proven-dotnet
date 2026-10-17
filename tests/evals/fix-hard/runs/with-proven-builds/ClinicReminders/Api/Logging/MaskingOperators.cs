using System.Text.RegularExpressions;
using Serilog.Enrichers.Sensitive;

namespace ClinicReminders.Api.Logging;

// Saudi mobile: 05XXXXXXXX, 5XXXXXXXX, +9665XXXXXXXX or 009665XXXXXXXX, in Western or Arabic-Indic digits.
public sealed class SaudiMobileMaskingOperator() : RegexMaskingOperator(@"(?<!\d)(?:\+966|00966|[0٠۰])?[5٥۵]\d{8}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "*******" + match.Value[^2..];
}

// Saudi national ID or iqama: 10 digits starting with 1 or 2. Keep the last four only. Arabic keyboards type
// Arabic-Indic digits: \d matches them, but the leading [12] must list them too.
public sealed class SaudiNationalIdMaskingOperator() : RegexMaskingOperator(@"(?<!\d)[12١٢۱۲]\d{9}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "******" + match.Value[^4..];
}
