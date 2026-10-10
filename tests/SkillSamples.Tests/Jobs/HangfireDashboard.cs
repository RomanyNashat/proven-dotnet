using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Jobs;

// The dashboard can retry, delete and trigger jobs: put it behind the same policy system as the API.
public sealed class DashboardPolicyFilter(string policy) : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, policy)).Succeeded;
    }
}

public static class HangfireDashboard
{
    public const string Policy = "JobsAdmin";

    public static IEndpointConventionBuilder MapJobsDashboard(this IEndpointRouteBuilder app) =>
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [],                                   // drop the default local-requests-only filter
            AsyncAuthorization = [new DashboardPolicyFilter(Policy)],
            DisplayStorageConnectionString = false
        });
}
