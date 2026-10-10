using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Quartz;

namespace SkillSamples.Scheduling;

// Behind an admin policy; every action checks the job exists, so a typo is a 404, not a silent no-op.
public static class JobAdminEndpoints
{
    public static RouteGroupBuilder MapJobAdmin(this IEndpointRouteBuilder app, string policy)
    {
        var group = app.MapGroup("/admin/jobs").RequireAuthorization(policy);
        group.MapPost("/{name}/trigger", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.TriggerJob(key, ct), ct));
        group.MapPost("/{name}/pause", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.PauseJob(key, ct), ct));
        group.MapPost("/{name}/resume", (string name, ISchedulerFactory f, CancellationToken ct) =>
            Act(f, name, (s, key) => s.ResumeJob(key, ct), ct));
        return group;
    }

    private static async Task<Results<Accepted, NotFound>> Act(
        ISchedulerFactory factory, string name, Func<IScheduler, JobKey, Task> action, CancellationToken ct)
    {
        var scheduler = await factory.GetScheduler(ct);
        var key = new JobKey(name);
        if (!await scheduler.CheckExists(key, ct))
            return TypedResults.NotFound();

        await action(scheduler, key);
        return TypedResults.Accepted((string?)null);
    }
}
