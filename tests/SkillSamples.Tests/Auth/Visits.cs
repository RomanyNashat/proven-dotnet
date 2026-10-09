using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SkillSamples.Auth;

public sealed record VisitRecord(int Id, string PatientSub, string Summary);

/// <summary>The caller may see a visit if it's theirs, or if they're a doctor.</summary>
public sealed class VisitAccessRequirement : IAuthorizationRequirement;

public sealed class VisitAccessHandler : AuthorizationHandler<VisitAccessRequirement, VisitRecord>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, VisitAccessRequirement requirement, VisitRecord visit)
    {
        if (context.User.FindFirstValue("sub") == visit.PatientSub || context.User.IsInRole("doctor"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class VisitEndpoints
{
    // Someone else's visit is NotFound, like a visit that doesn't exist: a 403 would tell the caller the id
    // exists, and ids are sequential ints (rules/security.md).
    public static async Task<Results<Ok<VisitRecord>, NotFound>> GetVisit(
        int id, ClaimsPrincipal user, IAuthorizationService authorization, IVisitStore visits, CancellationToken ct)
    {
        var visit = await visits.FindAsync(id, ct);
        if (visit is null || !(await authorization.AuthorizeAsync(user, visit, new VisitAccessRequirement())).Succeeded)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(visit);
    }
}

public interface IVisitStore
{
    Task<VisitRecord?> FindAsync(int id, CancellationToken ct);
}
