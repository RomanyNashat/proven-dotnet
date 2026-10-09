---
name: api-design
description: ASP.NET Core APIs — endpoints with injected handlers and TypedResults, ownership checks on every id, ProblemDetails that leak nothing, validation, X-API-Version/path versioning, keyset paging, per-caller rate limits, health checks. Core code tested in CI.
version: 3.0.0
---

# API Design

The model knows ASP.NET Core. This skill holds what's specific here: the rules, the versioning, and the
places where the common answer is wrong. The code marked as a sample is tested in CI
(`tests/SkillSamples.Tests/Api`).

Related: `localization`
(`x-language`, localized ProblemDetails text), `auth-patterns`, `nginx` (client IP and limits behind the
proxies), `cqrs-eventsourcing` (the handlers).

## 1. An endpoint

```csharp
public static class AppointmentEndpoints
{
    public static RouteGroupBuilder MapAppointmentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:int}", GetById).WithName("GetAppointment").WithSummary("One of the caller's appointments");
        group.MapPost("/", Book).WithName("BookAppointment");
        return group;   // 5-7 endpoints per group at most (rules/architecture.md)
    }

    private static async Task<Results<Ok<AppointmentDto>, NotFound>> GetById(
        int id, ICurrentUser user, IQueryHandler<GetAppointmentQuery, AppointmentDto?> handler, CancellationToken ct)
    {
        // The query filters by the caller (WHERE id = @id AND patient_id = @callerId). Ids are sequential
        // ints, so without this anyone can read the next record. Someone else's record is NotFound, not
        // Forbid: a 403 tells a caller the id exists.
        var dto = await handler.HandleAsync(new GetAppointmentQuery(id, user.PatientId), ct);
        return dto is null ? TypedResults.NotFound() : TypedResults.Ok(dto);
    }

    private static async Task<Results<Created<AppointmentDto>, ValidationProblem, Conflict<ProblemDetails>>> Book(
        BookAppointmentRequest request, ICurrentUser user,
        ICommandHandler<BookAppointment, Result<AppointmentDto, BookingError>> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new BookAppointment(user.PatientId, request.SlotId), ct);
        return result.Match<Results<Created<AppointmentDto>, ValidationProblem, Conflict<ProblemDetails>>>(
            dto => TypedResults.Created($"/api/appointments/{dto.Id}", dto),
            error => TypedResults.Conflict(new ProblemDetails { Title = error.Message }));   // expected failure: a result, not an exception
    }
}

// Program.cs: deny by default, open endpoints say so explicitly (rules/security.md)
app.MapGroup("/api/appointments").MapAppointmentEndpoints().RequireAuthorization().WithTags("Appointments");
```

Points that matter here:
- **Handlers are injected** into the endpoint; no mediator (`cqrs-eventsourcing`).
- **`TypedResults`** in Minimal APIs, **`ActionResult<T>`** in controllers (`rules/aspnetcore-rules.md`).
- **Expected failures are results** mapped to a status; exceptions are for bugs and outages only.
- **Every endpoint that takes an id checks the caller may see that record**, in the query itself.
- `CancellationToken` on every handler, passed down to the database.

## 2. Errors: ProblemDetails that say nothing internal

<!-- sample: tests/SkillSamples.Tests/Api/UnexpectedExceptionHandler.cs -->
```csharp
// For bugs and outages only. Expected failures (not found, not allowed, invalid) are results mapped to
// TypedResults in the endpoint, never exceptions (rules/architecture.md).
// The response says nothing about the exception: its message can carry patient data or internals
// (rules/security.md). The traceId is how support finds the full error in the logs.
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;   // the client went away: nothing to answer, nothing to alert on
        }

        logger.LogError(exception, "Unhandled {ExceptionType} on {Method} {Route}",
            exception.GetType().Name, http.Request.Method, http.GetEndpoint()?.DisplayName);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["traceId"] = http.TraceIdentifier },
            },
        });
    }
}
```

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
app.UseExceptionHandler();
```

Tested: an exception whose message contains a national ID returns a 500 ProblemDetails with a `traceId`
and neither the message nor the exception type.
- Never put `exception.Message` in `Detail`, in either a handler or a controller filter.
- Never set `Include Error Detail=true` in a production Npgsql connection string: PostgreSQL's
  unique-violation detail then carries the duplicate value (`Key (national_id)=(...)`) into exceptions and
  logs.
- **SQL Server always does this:** a unique violation (error 2627 or 2601) says `The duplicate key value is
  (...)` in the exception message, and there is no setting to turn it off. Map it by number
  (`SqlException.Number`) to a 409 without the message, and keep the message out of logs (`pii-masking`).
- Localized `title`/`detail` text comes from `localization`; the status and `type` stay the same per
  language.

## 3. Validation

- **FluentValidation** (the default here) or DataAnnotations with .NET 10's `AddValidation()`. Every request
  DTO is validated before its handler runs (`rules/security.md`).
- **String limits match the column** (`HasMaxLength(200)` ↔ `MaximumLength(200)`), so a long value is a
  400, not a database error.
- **Rules about time take `TimeProvider`**, injected into the validator; never `DateTime.UtcNow` inside
  an `IValidatableObject`.

```csharp
public sealed class BookAppointmentValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentValidator(TimeProvider time)
    {
        RuleFor(r => r.SlotId).GreaterThan(0);
        RuleFor(r => r.Notes).MaximumLength(200);
        RuleFor(r => r.PreferredFrom).GreaterThan(_ => time.GetUtcNow()).When(r => r.PreferredFrom.HasValue);
    }
}
```

## 4. Versioning

The API version comes from the **`X-API-Version` header or the path**, depending on the service. Read
both, so a service can move from one to the other without breaking callers:

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;   // existing callers that send nothing stay on v1
    o.ReportApiVersions = true;
    o.ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(), new HeaderApiVersionReader("X-API-Version"));
});
```

Routes (package `Asp.Versioning.Http`; controllers use `Asp.Versioning.Mvc` and `[ApiVersion]`). The
group needs `NewVersionedApi()` first; `HasApiVersion` on a plain `MapGroup` is not enough:

```csharp
var appointments = app.NewVersionedApi("Appointments");
appointments.MapGroup("/api/appointments").HasApiVersion(1.0).MapAppointmentEndpoints();      // header services
appointments.MapGroup("/api/v{version:apiVersion}/appointments").HasApiVersion(1.0).MapAppointmentEndpoints();   // path services
```

`App-Version` is the **mobile app's** version, not the API's. Use it to answer an old app differently
(a forced-update message), never as the API version. Adding a version is how a breaking change ships;
changing a response inside an existing version breaks installed apps.

## 5. Lists and paging

- **Cap the page size** on the server, whatever the client sends.
- **Keyset paging** (`WHERE id > @after ORDER BY id LIMIT @size`, return the next cursor) for anything that
  grows: appointments, visits, logs. `OFFSET` gets slower page by page (`rules/performance.md`).
- Project to a DTO with only the fields the screen needs; a list never returns whole patient records.

## 6. Rate limiting

<!-- sample: tests/SkillSamples.Tests/Api/PerClientRateLimits.cs -->
```csharp
public static class PerClientRateLimits
{
    public const string Otp = "otp";

    // One bucket per caller: the user id when signed in, otherwise the client IP (which is only the real
    // client behind nginx when ForwardedHeaders is set up; see the nginx skill). A limiter added without
    // a partition is ONE bucket shared by every caller of the endpoint.
    // These buckets live in each pod. For a limit that must hold across pods (OTP abuse, a paid
    // downstream), use the Redis limiter in redis-patterns.
    public static IServiceCollection AddPerClientRateLimits(this IServiceCollection services, int otpPerWindow = 5) =>
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(Otp, http => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPerWindow, Window = TimeSpan.FromMinutes(1) }));

            o.OnRejected = async (ctx, ct) =>
            {
                var http = ctx.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
                });
            };
        });

    private static string PartitionKey(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{http.Connection.RemoteIpAddress}";
}
```

```csharp
app.UseRateLimiter();   // after UseForwardedHeaders and authentication, so the partition key is the real caller
group.MapPost("/otp", SendOtp).RequireRateLimiting(PerClientRateLimits.Otp);
```

Tested: each caller has their own quota, and a rejected call is a 429 ProblemDetails with `Retry-After`.
The common version, `AddFixedWindowLimiter("api", ...)` with no partition, is one bucket that every user
of the endpoint shares.

## 7. OpenAPI

- With .NET 10's built-in `AddOpenApi()`: XML comments on handler **methods** (not lambdas) become the
  descriptions; `.WithSummary()` / `.WithDescription()` for the rest. **`.WithOpenApi(...)` is deprecated
  in .NET 10 (ASPDEPR002)**, and with warnings as errors it fails the build; use operation transformers.

## 8. Health checks

Packages `AspNetCore.HealthChecks.NpgSql` (or `AspNetCore.HealthChecks.SqlServer`) and
`AspNetCore.HealthChecks.Redis`.

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"])      // SQL Server: .AddSqlServer(connectionString, name: "sqlserver", tags: ["ready"])
    .AddRedis(redisConnection, name: "redis", tags: ["ready"]);

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();               // the process answers
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
```

- Liveness checks no dependencies: a database outage must not make Kubernetes restart every pod.
- Keep the default response (status only). A detailed writer lists dependency names, hosts and error
  messages; never on a path the ingress exposes.

## 9. Controllers

The same rules apply: thin, `[ApiController]`, attribute routing, sealed, handlers through
`[FromServices]` or the constructor, `ActionResult<T>`, `CreatedAtAction` for POST, a `CancellationToken`
last. The global `IExceptionHandler` above covers controllers too; don't add an exception filter that
builds its own error body.

## 10. Webhooks and API keys

```csharp
// Compare secrets in constant time: `!=` returns sooner the earlier the first wrong byte is, which leaks it.
var expected = Encoding.UTF8.GetBytes(options.Value.WebhookKey);
var provided = Encoding.UTF8.GetBytes(http.Request.Headers["X-Api-Key"].ToString());
if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return TypedResults.Unauthorized();
```

The key comes from the vault through options (`secret-management`), never `IConfiguration["ApiKey"]` in
the filter.

## 11. Review checklist (used by `code-reviewer`)
- An endpoint taking an id whose query doesn't filter by the caller.
- `exception.Message` (or `ex.ToString()`) in a response; `Include Error Detail` in a production
  connection string.
- An exception thrown for an expected failure (not found, not allowed, invalid).
- A rate limiter without a partition, or one partitioned by IP without `ForwardedHeaders`.
- A list endpoint without a server-side cap, or `OFFSET` paging on a growing table.
- `.WithOpenApi(...)` on .NET 10; a detailed health-check writer on a public path.
- `DateTime.UtcNow` in validation; a string field without a length that matches its column.
- A secret compared with `==` or `!=`.
