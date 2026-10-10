using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SkillSamples.Core;

public static class ServiceValidation
{
    // The host checks service lifetimes only in Development. In Production a singleton that takes a scoped
    // service (a DbContext, the current user) builds fine and then shares that one instance across every
    // request. Turn both checks on everywhere: a broken lifetime then stops the deploy at start-up.
    // ValidateOnBuild alone doesn't catch it; it needs ValidateScopes too.
    public static TBuilder ValidateServicesInEveryEnvironment<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        return builder;
    }
}
