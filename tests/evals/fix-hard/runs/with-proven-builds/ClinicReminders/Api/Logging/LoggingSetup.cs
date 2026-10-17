using ClinicReminders.Application;
using Microsoft.Extensions.Compliance.Redaction;
using Serilog;
using Serilog.Enrichers.Sensitive;
using Serilog.Extensions.Logging;

namespace ClinicReminders.Api.Logging;

public static class LoggingSetup
{
    /// <summary>
    /// Redaction runs in the logger factory that EnableRedaction installs, so Serilog is a provider under it,
    /// never the factory (UseSerilog replaces the factory and the classified fields never reach the redactor).
    /// The logger is built from the "Serilog" section when the container is built, so configuration added by
    /// the host (or a test) is seen.
    /// </summary>
    public static WebApplicationBuilder AddClassifiedSerilog(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddRedaction(r => r.SetRedactor<ErasingRedactor>(ClinicData.PatientIdentifier));
        builder.Logging.EnableRedaction();
        builder.Services.AddSingleton<ILoggerProvider>(sp => new SerilogLoggerProvider(
            new LoggerConfiguration()
                .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
                .AddPiiMasking()
                .CreateLogger(),
            dispose: true));
        return builder;
    }

    // The net for every line nobody classified: patterns find mobiles and IDs inside any logged value.
    // Setting MaskingOperators replaces the library's defaults, so the ones still wanted are listed again.
    public static LoggerConfiguration AddPiiMasking(this LoggerConfiguration config) => config
        .Enrich.WithSensitiveDataMasking(options =>
        {
            options.MaskValue = "***MASKED***";
            options.MaskingOperators =
            [
                new EmailAddressMaskingOperator(),
                new IbanMaskingOperator(),
                new CreditCardMaskingOperator(),
                new SaudiNationalIdMaskingOperator(),
                new SaudiMobileMaskingOperator(),
            ];
        });
}
