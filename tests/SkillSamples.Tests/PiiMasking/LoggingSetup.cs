using Serilog;
using Serilog.Enrichers.Sensitive;

namespace SkillSamples.PiiMasking;

public static class LoggingSetup
{
    // Two layers: the destructuring policy masks {@Objects} by property; the enricher catches PII inside
    // scalar values and strings by pattern. Setting MaskingOperators replaces the library's defaults, so
    // the ones still wanted (email, IBAN, card) are listed again.
    public static LoggerConfiguration AddPiiMasking(this LoggerConfiguration config) => config
        .Destructure.With<SensitiveFieldDestructuringPolicy>()
        .Enrich.WithSensitiveDataMasking(options =>
        {
            options.MaskValue = Masking.Full;
            options.MaskingOperators =
            [
                new EmailAddressMaskingOperator(),
                new IbanMaskingOperator(),
                new CreditCardMaskingOperator(),
                new SaudiNationalIdMaskingOperator(),
                new SaudiMobileMaskingOperator(),
                new MedicalRecordNumberMaskingOperator(),
            ];
        });
}
