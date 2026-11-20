using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.Registry;
using Polly.Timeout;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Resilience;

/// <summary>A downstream call that takes a while: on the real clock, or on the clock it's given.</summary>
public sealed class SlowDownstream(TimeSpan takes, TimeProvider? clock = null)
{
    public async Task<string> GetAsync(CancellationToken ct)
    {
        await Task.Delay(takes, clock ?? TimeProvider.System, ct);
        return "ok";
    }
}

public sealed class FakeClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private const string Gateway = "gateway";

    // A 5-second timeout registered as a service registers its pipelines, with the test's clock in DI:
    // Polly's timeouts, delays and break durations then read that clock, which is what lets a test move them.
    private static (ResiliencePipeline Pipeline, ServiceProvider Sp) PipelineOn(FakeTimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddResiliencePipeline(Gateway, pipeline => pipeline.AddTimeout(TimeSpan.FromSeconds(5)));
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ResiliencePipelineProvider<string>>().GetPipeline(Gateway), sp);
    }

    // The trap: a helper that "waits" by moving the fake clock a second every 50 ms of real time, while a
    // real one-second call is in flight. To the pipeline it took twenty seconds, so the 5-second
    // timeout fires after a quarter of a second, in the test only.
    private static async Task<T> WaitTickingAsync<T>(FakeTimeProvider clock, Task<T> work)
    {
        while (!work.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(work, Task.Delay(50));
        }
        return await work;
    }

    [Fact]
    public async Task Trap_ClockMovedOnARealTimer_TimesOutACallThatTakesOneSecond()
    {
        var clock = new FakeTimeProvider(Start);
        var (pipeline, sp) = PipelineOn(clock);
        using var _ = sp;
        var api = new SlowDownstream(TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();

        var call = pipeline.ExecuteAsync(async token => await api.GetAsync(token)).AsTask();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => WaitTickingAsync(clock, call));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"real time was only {watch.Elapsed}");
    }

    [Fact]
    public async Task SlowCallOnTheSameClock_PassesUnderTheTimeout_AndFailsOverIt()
    {
        // The fix: the slow thing waits on the clock the test moves, and the test moves it by the step it
        // means. 4 seconds is under the 5-second timeout, 6 is over, whatever the machine's speed.
        var clock = new FakeTimeProvider(Start);
        var (pipeline, sp) = PipelineOn(clock);
        using var _ = sp;

        var quick = pipeline.ExecuteAsync(async token => await new SlowDownstream(TimeSpan.FromSeconds(4), clock).GetAsync(token)).AsTask();
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("ok", await quick);

        var slow = pipeline.ExecuteAsync(async token => await new SlowDownstream(TimeSpan.FromSeconds(6), clock).GetAsync(token)).AsTask();
        clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => slow);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ATestWaitsForASlowGateway_WithoutTurningItIntoATimeout()
    {
        // Given: the SMS gateway answers in 300 ms of real time, behind the service's 5-second attempt
        // timeout, and the test's clock is fake because the service schedules its sends by it
        var clock = new FakeTimeProvider(Start);
        var (pipeline, sp) = PipelineOn(clock);
        using var _ = sp;
        var gateway = new SlowDownstream(TimeSpan.FromMilliseconds(300));

        // When: the test waits for the send in real time, and moves the fake clock only for the schedule
        var send = pipeline.ExecuteAsync(async token => await gateway.GetAsync(token)).AsTask();
        var sent = await send.WaitAsync(TimeSpan.FromSeconds(10));

        // Then: the send goes through; the fake clock never moved, so the timeout never saw it as slow
        Assert.Equal("ok", sent);
        Assert.Equal(Start, clock.GetUtcNow());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_SlimImage_TheClockStepsAreTheSame()
    {
        ProductionConditions.Require();
        var clock = new FakeTimeProvider(Start);
        var (pipeline, sp) = PipelineOn(clock);
        using var _ = sp;

        var slow = pipeline.ExecuteAsync(async token => await new SlowDownstream(TimeSpan.FromSeconds(6), clock).GetAsync(token)).AsTask();
        clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => slow);
    }
}
