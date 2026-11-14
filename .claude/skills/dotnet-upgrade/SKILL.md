---
name: dotnet-upgrade
description: "Move a service from .NET 8 or 9 to .NET 10 and turn on nullable reference types: the order of work, the breaking changes that bite, proving nothing changed."
version: 1.1.0
---

# Upgrading to .NET 10

.NET 8 and .NET 9 both leave support on **10 November 2026**. After that there are no security fixes, and
the zero-vulns gate (`zero-vulns`) will start failing on packages that only get fixed for 10. Upgrade one
service at a time, one merge request each, with its tests green before and after. To find every service
still on 8 or 9, run `known-traps` over the folder that holds them: `framework-out-of-support` names each one.

## 1. Before you change anything

- **The tests are the safety net.** Run them, and note the count. If coverage is thin around the code
  the upgrade touches (EF queries, serialization, auth), add characterization tests first (`test-coverage`).
- **Inventory**, so nothing is upgraded by surprise:
  ```bash
  dotnet --list-sdks
  dotnet list package --include-transitive --format json > packages-before.json
  grep -rn "TargetFramework\|LangVersion\|Nullable\|TreatWarningsAsErrors" --include=*.csproj --include=*.props .
  ```
- Check that every third-party package has a version that supports `net10.0`. A package that doesn't is
  the blocker to solve first, not halfway through.
- **Run the preflight scan** and put its table in the merge request. It's the list of breaks this service
  meets, so the release notes are read for those and not end to end:
  ```bash
  python ~/.claude/skills/dotnet-upgrade/scripts/upgrade_preflight.py path/to/Service
  ```
  On Windows, `py -3` with the same arguments. Read-only, standard library only, no build; `--json` for a
  pipeline. Each finding is a line to read, not a proof (the table in §3, "What the preflight scan finds").

## 2. The order of work

1. **SDK:** `global.json` → the 10.0 SDK, `"rollForward": "latestFeature"`.
2. **Target framework:** `net10.0` in `Directory.Build.props` (or each `.csproj`). This also moves the
   language to C# 14.
3. **Packages that ship with the framework, all together:** `Microsoft.AspNetCore.*`,
   `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*` → 10.x; Npgsql's EF provider → 10.x. A mix of
   9.x and 10.x of these is where the strangest errors come from.
4. **Build, then read every warning.** SDK 10 brings new ones on purpose (below).
5. **Tests**, including the story and production tests: the base image usually changes with the
   framework, and that's where ICU and tzdata go missing (`localization`).
6. **The image:** the runtime image tag moves to 10.0. Where a pipeline builds the image, change it there.

Don't combine the upgrade with a refactor. If something breaks, the diff should only be the upgrade.

## 3. What breaks in a backend service

### C# 14 and EF Core: upgrade them together

C# 14 lets arrays convert to spans implicitly, so `ids.Contains(x)` on an array now binds to the span
overload (`MemoryExtensions.Contains`). Inside a LINQ query that's an expression tree, and EF Core 8 and 9
can't translate it. EF Core 10 can. So the target framework and EF Core move in the same change, never
the framework first.

```csharp
// Fails to translate on EF Core 8/9 once the project is C# 14; works on EF Core 10.
var query = context.Visits.Where(v => requested.Contains(v.Id));
```

Tested as a story on EF Core 10, on both engines: a list of ids from a request still filters correctly
after the upgrade. On SQL Server the list is no longer sent as JSON (`OPENJSON`), so the database's
compatibility level no longer has to be 130 or higher for it.

### EF Core 10: `ExecuteUpdate` takes a plain lambda

<!-- sample: tests/SkillSamples.Tests/EfCore/VisitReview.cs -->
```csharp
public static class VisitReview
{
    /// <summary>
    /// EF Core 10 takes a plain lambda here, so a setter can depend on a condition. Before 10 it took an
    /// expression tree: a conditional setter meant one call per combination, or building the tree by hand.
    /// The owner filter still applies, and the audit interceptor still doesn't run (efcore-patterns §6).
    /// </summary>
    public static Task<int> MarkReviewedAsync(
        VisitsDbContext db, IReadOnlyCollection<int> ids, string? note, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.Visits
            .Where(v => ids.Contains(v.Id))
            .ExecuteUpdateAsync(set =>
            {
                set.SetProperty(v => v.UpdatedAt, now);
                set.SetProperty(v => v.UpdatedBy, db.CallerId);
                if (note is not null)
                {
                    set.SetProperty(v => v.Notes, note);
                }
            }, ct);
    }
}
```

Tested on both engines: with a note, both columns change; without one, the note is left as it was. Code
that built the setter expression by hand (`Expression<Func<SetPropertyCalls<T>, ...>>`) no longer compiles:
replace it with the lambda above.

### SDK 10: new warnings on the first build

The first two were seen in this repo's own builds; the third is from the .NET 10 release notes.

- **Transitive packages are audited by default.** A vulnerable package three levels down is now an
  `NU1902`–`NU1904` warning, and an error under warnings-as-errors. Expect new ones on the first build:
  upgrade the parent that brings it first (`dotnet nuget why`, `parent_fix.py`), and pin the fixed version
  directly only when no parent release brings it, with a comment naming the advisory and why (`zero-vulns`).
- **`NU1510`: a package the framework already provides.** References such as `System.Text.Json` or
  `System.Memory` that a .NET 10 project gets from the framework are flagged. Remove them; if an old
  version was pinned for a vulnerability, the framework's copy is the newer one anyway.
- **`System.Linq.Async`:** .NET 10 has async LINQ built in (`System.Linq.AsyncEnumerable`). With the old
  package referenced as well, calls such as `ToListAsync()` on an `IAsyncEnumerable<T>` become ambiguous.
  Remove the package.

### .NET 10: an exception handler that returns `true` stops the error log

On 8, `UseExceptionHandler` logged the exception and then called the handler. On 10, a handler that
returns `true` means the exception was dealt with, and the middleware writes no diagnostics for it. A
handler written for 8 that only shapes the response leaves the error out of the logs, and so out of Kibana
and APM. Tested as a story: with a handler that doesn't log, a failed request leaves no error in the logs;
with `api-design`'s `UnexpectedExceptionHandler`, which logs the exception itself, the error is there with
its exception. The fix is that handler: log in the handler, then answer.

### .NET 10: a worker's set-up before its first `await` no longer runs before start-up ends

On 8 the host ran `ExecuteAsync` up to its first `await` before it carried on starting, so set-up written
there (a cache filled, a connection opened) was done before the service took traffic. On 10 all of
`ExecuteAsync` runs as a Task: tested, a worker whose set-up blocks before its first `await` doesn't hold
up `StartAsync`, and the set-up isn't done when the host has started. Work that has to come first moves
to `StartAsync`, which the host awaits:

<!-- sample: tests/SkillSamples.Tests/Upgrade/WarmUpWorker.cs -->
```csharp
/// <summary>
/// On .NET 10 all of ExecuteAsync runs as a Task, so the code before its first await no longer runs before
/// the host has started. Work that must be done first goes in StartAsync, which the host awaits; the loop
/// stays in ExecuteAsync.
/// </summary>
public sealed class WarmUpWorker(IWarmUp warmUp) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await warmUp.RunAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
```

Tested: with the set-up in `StartAsync`, it is done when the host has started.

### Npgsql 10: `date` is `DateOnly`, `time` is `TimeOnly`

Where nothing names the type (`ExecuteScalar`, `GetValue`, `object`), Npgsql 10 returns a `date` column as
`DateOnly` and a `time` column as `TimeOnly`, not `DateTime` and `TimeSpan`. Tested on PostgreSQL: an
untyped read returns `DateOnly` and `TimeOnly`; a Dapper record with `DateOnly` and `TimeOnly` properties
reads the row; `GetFieldValue<DateTime>` still reads a date as a `DateTime`. So map `date` to `DateOnly`
in records and DTOs, or name the type where the code reads it.

### Behind nginx: forwarded headers need the proxies named

`X-Forwarded-*` is applied only when the request comes from a trusted proxy, and only loopback is trusted
by default. Behind the ingress and nginx, in another pod, the app sees the proxy as the client and `http`
as the scheme. `KnownNetworks` and its `IPNetwork` are obsolete on 10: name the pod network in
`KnownIPNetworks` with `System.Net.IPNetwork`. Tested in `nginx` (`ConfigureBehindNginx`): with the
defaults the app sees the proxy and `http`; configured, it sees the client and `https`, and a client's own
`X-Forwarded-For` isn't believed.

### What the preflight scan finds

These come from Microsoft's breaking-change lists for .NET 9 and 10, ASP.NET Core 9 and 10 and EF Core 9
and 10, and from Npgsql's release notes for 9 and 10. The sections above are tested here, and the rows
that one of them covers say "tested". The others are what the lists say, read against our stack; the scan
only points at the line. Worst first:

| Check | Severity | Since | What changed |
|---|---|---|---|
| `binary-formatter` | breaks | .NET 9 | `BinaryFormatter` always throws |
| `versionless-package` | breaks | SDK 10 | a `PackageReference` without a version is an error (outside Central Package Management) |
| `system-linq-async` | breaks | .NET 10 | `System.Linq.Async` makes async LINQ calls ambiguous (above) |
| `ef-hasdata-dynamic` | breaks | EF Core 9 | `HasData` with `DateTime.Now` or `Guid.NewGuid()`: the model changes on every build |
| `forwarded-known-networks` | obsolete | ASP.NET Core 10 | `KnownNetworks` and its `IPNetwork`; use `KnownIPNetworks` with `System.Net.IPNetwork` (tested, above) |
| `with-openapi` | obsolete | ASP.NET Core 10 | `.WithOpenApi()` on an endpoint |
| `action-context-accessor` | obsolete | ASP.NET Core 10 | `IActionContextAccessor` |
| `webhost-builder` | obsolete | ASP.NET Core 10 | `WebHostBuilder`, `IWebHost`, `WebHost` |
| `framework-package-reference` | obsolete | SDK 10 | `NU1510` for a package the framework provides (above) |
| `forwarded-headers-trust` | behaviour | ASP.NET Core 9 | `X-Forwarded-*` from a proxy not named as trusted is ignored: behind nginx the client IP and scheme are lost (tested, above) |
| `exception-handler-diagnostics` | behaviour | ASP.NET Core 10 | an `IExceptionHandler` returning `true` emits no exception diagnostics, so the error can drop out of logs and APM (tested, above) |
| `npgsql-connection-lifetime` | behaviour | Npgsql 9 | `Connection Lifetime` defaults to an hour |
| `sqlserver-application-name` | behaviour | EF Core 10 | EF adds an `Application Name` to a SQL Server connection string that has none |
| `azure-sql-json` | behaviour | EF Core 10 | with `UseAzureSql`, JSON maps to the `json` type, and existing columns get an `ALTER` |
| `background-service-start` | behaviour | .NET 10 | all of `ExecuteAsync` runs as a Task, so code before the first `await` no longer runs before the host has started (tested, above) |
| `npgsql-date-read` | check | Npgsql 10 | untyped reads return `date` as `DateOnly` and `time` as `TimeOnly` (tested, above) |
| `image-tag` | check | .NET 10 | the tag moves to 10.0; the default images are Ubuntu since 10, and 9 dropped zlib |

Not found by the scan, so read them in the lists when the service uses the area:
- configuration keeps keys whose value is null (10), and an empty environment variable is a value (9);
- `HttpClientFactory` redacts header values and the query string in its logs, and uses `SocketsHttpHandler` (9);
- the default trace propagator is W3C (10), and `ActivitySource` sampling changed;
- System.Text.Json fails on conflicting property names (10);
- `dotnet` CLI: `--interactive` is on in a terminal, `dotnet new sln` writes `.slnx`, and the terminal logger
  is the default (9);
- EF Core 10 simplifies SQL parameter names, so cached plans recompile after the deploy;
- Npgsql 10 validates TLS against the given root CA only, and renames its metrics and spans;
- `DOTNET_ICU_VERSION_OVERRIDE` and `DOTNET_OPENSSL_VERSION_OVERRIDE` replace the old variable names (10).

Write down in the merge request which ones applied.

## 4. Nullable reference types in an existing service

Turning `<Nullable>enable</Nullable>` on for a whole project at once produces hundreds of warnings and
the temptation to silence them with `!`. Instead:

1. **Per project, warnings first:** `<Nullable>warnings</Nullable>` shows the problems without changing
   what the annotations mean, so nothing breaks while you look.
2. **Fix from the edges inward:** request and response DTOs, then the domain, then services. A DTO's
   property is either `required` or nullable, decided by the contract, not by what silences the compiler.
3. **Then `enable`, and make the main warnings errors** so they can't come back:
   ```xml
   <Nullable>enable</Nullable>
   <WarningsAsErrors>$(WarningsAsErrors);CS8600;CS8602;CS8603;CS8618;CS8625</WarningsAsErrors>
   ```
4. **Files not done yet** can opt out with `#nullable disable` at the top, as a tracked list that only
   shrinks. A new file never starts with it.

Rules while doing it:
- `!` only where the code has just proven non-null in a way the compiler can't see, with a comment.
  Never to make a warning go away.
- `[NotNullWhen(true)]` on `TryGet...` methods, `[MemberNotNull]` on initializers, rather than `!` at
  every caller.
- Don't change behavior in the same change. Adding a null check that throws where the code used to carry
  on is a behavior change: it gets its own test and its own commit.

## 5. Proving nothing changed

- The test count after is the test count before, all green.
- `dotnet list package --include-transitive --format json > packages-after.json` and compare: every
  version change is one you meant.
- The service starts on the new image and answers its health check; the production tests pass under the
  slim-image conditions.
- For a refactor-sized nullable change, run it through `remediation-parity`.

## Rules
- One service per merge request; tests green before and after.
- Target framework, framework packages and EF Core move together.
- Read every new warning; pin or remove, never suppress.
- Nullable: warnings first, edges inward, `enable` with errors last; no `!` to silence.
