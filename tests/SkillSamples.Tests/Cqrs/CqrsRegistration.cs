using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Cqrs;

public static class CqrsRegistration
{
    /// <summary>
    /// Registers every command and query handler in the assembly, wrapped in its decorators.
    /// Commands: logging → validation → transaction → handler. Queries: logging → handler.
    /// </summary>
    public static IServiceCollection AddCqrsHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
        foreach (var type in handlerTypes)
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                var args = contract.GetGenericArguments();
                if (definition == typeof(ICommandHandler<,>))
                {
                    services.AddScoped(type);
                    services.AddScoped(contract, sp => Wrap(sp, sp.GetRequiredService(type), args,
                        typeof(TransactionDecorator<,>), typeof(ValidationDecorator<,>), typeof(LoggingCommandDecorator<,>)));
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    services.AddScoped(type);
                    services.AddScoped(contract, sp => Wrap(sp, sp.GetRequiredService(type), args,
                        typeof(LoggingQueryDecorator<,>)));
                }
            }
        }

        return services;
    }

    // Innermost first: each decorator receives the one built before it.
    private static object Wrap(IServiceProvider sp, object handler, Type[] args, params Type[] decorators) =>
        decorators.Aggregate(handler, (inner, decorator) =>
            ActivatorUtilities.CreateInstance(sp, decorator.MakeGenericType(args), inner));
}
