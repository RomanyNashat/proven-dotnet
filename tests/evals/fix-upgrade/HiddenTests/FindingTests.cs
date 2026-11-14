using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiddenTests;

/// <summary>One test per finding. Each is about the service on .NET 10, so each first requires the copy to be built for it.</summary>
public sealed class FindingTests
{
    private const string Day = "2026-11-16";

    internal static void RequireDotNet10()
    {
        var framework = typeof(Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "";
        Assert.True(framework == ".NETCoreApp,Version=v10.0", $"the service is built for {framework}, not .NET 10");
    }

    private static int Major(Type type) => type.Assembly.GetName().Version!.Major;

    [Fact(DisplayName = "U01 the service targets .NET 10, with ASP.NET Core, EF Core and Npgsql on 10")]
    public void U01_OnDotNet10()
    {
        RequireDotNet10();
        Assert.True(Major(typeof(JwtBearerHandler)) >= 10, $"JwtBearer {typeof(JwtBearerHandler).Assembly.GetName().Version}");
        Assert.True(Major(typeof(Microsoft.EntityFrameworkCore.DbContext)) >= 10, $"EF Core {typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.GetName().Version}");
        Assert.True(Major(typeof(NpgsqlConnection)) >= 10, $"Npgsql {typeof(NpgsqlConnection).Assembly.GetName().Version}");
    }

    [Fact(DisplayName = "U02 an unexpected error is still logged, with its exception")]
    public async Task U02_ErrorLogged()
    {
        RequireDotNet10();
        await using var pod = new Pod();
        var client = pod.As(7);
        var visit = await pod.BookAsync(client, 1, Day);
        Pg.Execute(pod.ConnectionString, "DROP TABLE visit");

        using var response = await client.GetAsync($"/visits/{visit}/day");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(pod.Logs.GetSnapshot(), r => r.Level >= LogLevel.Error && r.Exception is not null);
    }

    [Fact(DisplayName = "U03 the clinic list is filled before the service takes traffic")]
    public async Task U03_WarmBeforeTraffic()
    {
        RequireDotNet10();
        // The clinic list takes a while to load (a cold database after a deploy).
        var source = new SlowSource(TimeSpan.FromSeconds(2));
        await using var pod = new Pod(source);

        var clinics = await pod.As(7).GetFromJsonAsync<ClinicEntry[]>("/clinics");

        Assert.Equal(["Cardiology", "Dermatology"], clinics!.Select(c => c.Name));
    }

    [Fact(DisplayName = "U04 a visit's day still reads on Npgsql 10")]
    public async Task U04_VisitDay()
    {
        RequireDotNet10();
        Assert.True(Major(typeof(NpgsqlConnection)) >= 10, $"Npgsql {typeof(NpgsqlConnection).Assembly.GetName().Version}");
        await using var pod = new Pod();
        var client = pod.As(7);
        var visit = await pod.BookAsync(client, 1, Day);

        var day = await client.GetFromJsonAsync<DayView>($"/visits/{visit}/day");

        Assert.Equal(Day, day!.Day);
    }

    [Fact(DisplayName = "U05 behind nginx the service still sees the client's address")]
    public async Task U05_ClientAddress()
    {
        RequireDotNet10();
        await using var pod = new Pod();
        var client = pod.As(7);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9, 10.42.1.5");

        var caller = await client.GetFromJsonAsync<CallerView>("/whoami");

        Assert.Equal("203.0.113.9", caller!.ClientIp);
    }

    [Fact(DisplayName = "U06 the waiting-room queue is served, and served again from the cache")]
    public async Task U06_QueueSnapshot()
    {
        RequireDotNet10();
        await using var pod = new Pod();
        var client = pod.As(7);
        await pod.BookAsync(client, 1, Day);
        await pod.BookAsync(pod.As(8), 1, Day);

        var first = await client.GetFromJsonAsync<QueueView>($"/clinics/1/queue?day={Day}");
        var second = await client.GetFromJsonAsync<QueueView>($"/clinics/1/queue?day={Day}");

        Assert.Equal([1, 2], first!.Positions);
        Assert.Equal([1, 2], second!.Positions);
    }

    [Fact(DisplayName = "U07 System.Linq.Async is gone (.NET 10 has async LINQ built in)")]
    public void U07_NoLinqAsync()
    {
        RequireDotNet10();
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "System.Linq.Async.dll")), "System.Linq.Async.dll is still deployed with the service");
    }

    private sealed class SlowSource(TimeSpan delay) : IClinicSource
    {
        public IReadOnlyList<ClinicEntry> Load()
        {
            using var wait = new ManualResetEventSlim();
            wait.Wait(delay);
            return [new ClinicEntry(1, "Cardiology"), new ClinicEntry(2, "Dermatology")];
        }
    }

    private sealed record DayView(string Day);
    private sealed record CallerView(string ClientIp);
    private sealed record QueueView(int ClinicId, string Day, int[] Positions);
}
