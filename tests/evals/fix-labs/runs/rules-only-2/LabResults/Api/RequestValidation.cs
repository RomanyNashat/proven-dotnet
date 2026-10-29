using System.ComponentModel.DataAnnotations;

namespace LabResults.Api;

/// <summary>Runs a request's DataAnnotations before it reaches the handler's logic.</summary>
public static class RequestValidation
{
    /// <summary>Validates every annotated property of <paramref name="request"/>.</summary>
    /// <param name="request">The bound request body.</param>
    /// <param name="errors">The messages per property, in the shape <c>ValidationProblem</c> takes.</param>
    /// <returns>True when the request is valid.</returns>
    public static bool TryValidate(object request, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(member => (member, message: r.ErrorMessage ?? "Invalid.")))
            .GroupBy(e => e.member, e => e.message)
            .ToDictionary(g => g.Key, g => g.ToArray());
        return errors.Count == 0;
    }
}
