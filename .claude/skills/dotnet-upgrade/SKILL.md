---
name: dotnet-upgrade
description: Moving a service from .NET 8 or 9 to .NET 10 (C# 14, EF Core 10, SDK 10) and turning on nullable reference types in an existing codebase — the order of work, the breaking changes that bite a backend service, and how to prove nothing changed. EF Core 10 parts tested in CI on PostgreSQL and SQL Server.
version: 1.0.1
---

# Upgrading to .NET 10

.NET 8 and .NET 9 both leave support on **10 November 2026**. After that there are no security fixes, and
the zero-vulns gate (`zero-vulns`) will start failing on packages that only get fixed for 10. Upgrade one
service at a time, one merge request each, with its tests green before and after.

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

### Read the release notes for the rest

The breaking-change lists for ASP.NET Core 9 and 10, EF Core 9 and 10, and the SDK are long. Read the
sections for what the service uses (authentication, OpenAPI, serialization, EF providers) before the
upgrade, and write down in the merge request which ones applied. What isn't tested here isn't claimed here.

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
