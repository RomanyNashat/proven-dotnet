using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SkillSamples.PiiMasking;

public static class RedactionSetup
{
    // Redaction runs in the logger factory that EnableRedaction installs. Serilog has to be a provider
    // under it (AddSerilog). UseSerilog() replaces the factory, and the classified fields never reach the
    // redactor.
    public static IServiceCollection AddClassifiedLogging(this IServiceCollection services, Action<ILoggingBuilder> providers) => services
        .AddRedaction(redaction => redaction.SetRedactor<StarRedactor>(ClinicData.PatientIdentifier))
        .AddLogging(logging =>
        {
            logging.EnableRedaction();
            providers(logging);
        });
}
