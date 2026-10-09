using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace SkillSamples.PiiMasking;

// Masks sensitive properties when an object is logged with {@Object}. Types with nothing sensitive are
// left to Serilog's default handling, and the per-type plan is computed once.
public sealed class SensitiveFieldDestructuringPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Secret", "Token", "ApiKey", "AccessToken", "RefreshToken", "Authorization",
        "CardNumber", "Cvv", "NationalId", "Iqama", "PhoneNumber", "MobileNumber", "Mobile", "Email",
        "DateOfBirth", "Dob", "MedicalRecordNumber", "Mrn", "Diagnosis", "Prescription",
    };

    private static readonly ConcurrentDictionary<Type, Plan?> Plans = new();

    public bool TryDestructure(
        object value, ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        var plan = Plans.GetOrAdd(value.GetType(), BuildPlan);
        if (plan is null)
        {
            result = null;
            return false;
        }

        var properties = new List<LogEventProperty>(plan.Properties.Length);
        foreach (var (property, strategy) in plan.Properties)
        {
            var propertyValue = property.GetValue(value);
            LogEventPropertyValue logged = strategy is { } s
                ? new ScalarValue(Masking.Mask(propertyValue?.ToString(), s))
                : propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true);
            properties.Add(new LogEventProperty(property.Name, logged));
        }

        result = new StructureValue(properties, plan.TypeTag);
        return true;
    }

    private static Plan? BuildPlan(Type type)
    {
        // Scalars, framework types (DateTime, Guid, Uri...) and collections keep Serilog's own handling.
        // Anonymous types have no namespace and are checked like your own types.
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type)
            || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) == true)
        {
            return null;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => (Property: p, Strategy: StrategyFor(p)))
            .ToArray();
        return properties.Any(p => p.Strategy is not null) ? new Plan(type.Name.StartsWith("<>", StringComparison.Ordinal) ? null : type.Name, properties) : null;
    }

    private static MaskingStrategy? StrategyFor(PropertyInfo property) =>
        property.GetCustomAttribute<SensitiveAttribute>()?.Strategy
        ?? (SensitiveNames.Contains(property.Name) ? MaskingStrategy.Full : null);

    private sealed record Plan(string? TypeTag, (PropertyInfo Property, MaskingStrategy? Strategy)[] Properties);
}
