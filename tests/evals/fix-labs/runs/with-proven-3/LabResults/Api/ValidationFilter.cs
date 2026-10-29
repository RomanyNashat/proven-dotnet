using FluentValidation;

namespace LabResults.Api;

/// <summary>Validates the endpoint's <typeparamref name="T"/> argument before the handler runs.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is not { } argument)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["A request body is required."] });
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : TypedResults.ValidationProblem(result.ToDictionary());
    }
}
