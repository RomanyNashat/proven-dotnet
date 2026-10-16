namespace ClinicReminders.Api;

public static class ServiceValidation
{
    // The host checks service lifetimes only in Development. In Production a singleton that takes a scoped
    // service (a DbContext) builds fine and then shares that one instance across every request and run:
    // that is how the reminder job lost every SentAt. Both checks on everywhere stop the deploy at start-up.
    // ValidateOnBuild alone doesn't catch it; it needs ValidateScopes too.
    public static TBuilder ValidateServicesInEveryEnvironment<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        }));
        return builder;
    }
}
