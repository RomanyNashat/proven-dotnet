# api-design — evals

Mechanical checks only (see `skill-evals`). Written when the skill was rewritten (v3.0.0) to A/B the old
version against the new one. Each case targets a defect found in the old version.

### Case 1: an endpoint that takes an id
**Prompt:** Write a Minimal API endpoint `GET /api/appointments/{id}` that returns one appointment to the signed-in patient. Show the endpoint code only.
**Must:** the query filters by the caller (patient/owner) as well as the id; another patient's record is
not returned; `TypedResults`; the handler is injected (no mediator library).
**Must not:** load by id alone and return it.

### Case 2: rate limiting the OTP endpoint
**Prompt:** Add rate limiting to `POST /api/otp` so a client can request at most 5 codes a minute. Show the registration.
**Must:** the limit is per caller (partitioned by user or client IP); mention that the client IP needs
forwarded headers behind a proxy, or that in-memory limits are per pod.
**Must not:** a single unpartitioned limiter (`AddFixedWindowLimiter("name", ...)` applied to the endpoint).

### Case 3: global error handling
**Prompt:** Write the global exception handling for an ASP.NET Core 10 API. Show the code.
**Must:** ProblemDetails; a trace or correlation id; the response contains no exception message or type.
**Must not:** `Detail = exception.Message` (or `ex.ToString()`) in the response.

### Case 4: API versioning
**Prompt:** Set up API versioning. The mobile app sends the version in an `X-API-Version` header; some older services use `/v1/` in the path. Show the registration.
**Must:** read the `X-API-Version` header (combined with the path segment reader).
**Must not:** use the URL segment only.

### Case 5: health checks for Kubernetes
**Prompt:** Add liveness and readiness health checks for a service that uses PostgreSQL and Redis. Show the code.
**Must:** liveness checks no dependencies; readiness checks PostgreSQL and Redis.
**Must not:** a detailed response writer (`UIResponseWriter` or one that lists dependency errors) on these endpoints.

### Case 6: validating a request
**Prompt:** Write validation for `BookAppointmentRequest { int SlotId; string? Notes; DateTimeOffset? PreferredFrom }`: notes are stored in a varchar(200) column, and PreferredFrom must be in the future.
**Must:** a max length of 200 on Notes; "in the future" compared against an injected `TimeProvider`.
**Must not:** `DateTime.UtcNow` / `DateTimeOffset.UtcNow` / `DateTime.Now` in the rule.

### Case 7: endpoint documentation on .NET 10
**Prompt:** On .NET 10, add a summary and a description to the OpenAPI document for `GET /api/appointments/{id}`. Show the code.
**Must:** `.WithSummary()` / `.WithDescription()`, XML comments on the handler method, or an operation transformer.
**Must not:** `.WithOpenApi(...)` (deprecated in .NET 10, ASPDEPR002).

## Results

### 2026-10-05 — A/B, old skill (v2, 25.0 KB) vs rewrite (v3.0.0, 13.0 KB)

One fresh subagent per case and version (14 runs), graded against the lines above.

| Case | Old skill | New skill |
|---|---|---|
| 1 id endpoint | pass, but the agent added the ownership check from `rules/security.md`; the skill's example had none | pass |
| 2 OTP rate limit | pass, but the agent replaced the skill's single shared bucket with a partitioned one | pass |
| 3 errors | pass | pass |
| 4 versioning | pass, but the agent added the header reader against the skill's path-only example | pass |
| 5 health checks | **fail**: `UIResponseWriter` on `/health/ready`, copied from the skill | pass |
| 6 validation | pass | pass |
| 7 OpenAPI on .NET 10 | pass; the agent avoided the skill's `WithOpenApi` | pass |

Old 6/7, new 7/7. Most of the old version's passes came from the agent overriding the skill with the
always-loaded rules; where the rules said nothing (case 5), the skill's defect reached the answer. About
6.3k fewer tokens per load (≈104.1k vs ≈110.4k per subagent).

After the results, two notes from the new-skill runs were added to the skill (not a change to any case):
versioned route groups need `NewVersionedApi()` (an old-skill run said `HasApiVersion` on a plain
`MapGroup` fails in Asp.Versioning 8), and the health-check package names.
