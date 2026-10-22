using System.Text.RegularExpressions;
using Serilog;
using Serilog.Enrichers.Sensitive;

namespace HomeReadings.Infrastructure;

/// <summary>
/// The net for log lines nobody thought about: national IDs and other identifiers inside any logged value
/// are masked by pattern. Code still logs ids, never patient data.
/// </summary>
public static class LoggingSetup
{
    public static LoggerConfiguration AddPiiMasking(this LoggerConfiguration config) => config
        .Enrich.WithSensitiveDataMasking(options =>
        {
            options.MaskingOperators =
            [
                new EmailAddressMaskingOperator(),
                new IbanMaskingOperator(),
                new CreditCardMaskingOperator(),
                new SaudiNationalIdMaskingOperator(),
            ];
        });
}

/// <summary>
/// A Saudi national ID or iqama: 10 digits starting with 1 or 2, in ASCII or Arabic-Indic digits. Keeps the
/// last four; the first digit (citizen or resident) is itself personal data.
/// </summary>
public sealed class SaudiNationalIdMaskingOperator() : RegexMaskingOperator(@"(?<!\d)[12١٢۱۲]\d{9}(?!\d)")
{
    protected override string PreprocessMask(string mask, Match match) => "******" + match.Value[^4..];
}
