---
name: api-contract-evolution
description: Changing an API that installed mobile apps call — what breaks an app you can't update (a new enum value, a removed field, a new required request field), expand, migrate, contract, Deprecation and Sunset headers, and oasdiff in CI set to catch what its defaults let through. Tested in CI.
version: 1.0.0
---

# Changing an API that installed apps call

A web page updates with the release. An app on a phone doesn't: some patients stay on an old version for
months. So every response has to stay readable by every app version you still support, and every request
those versions send has to keep working.

## What breaks an installed app

Each row is tested (`Contracts/ContractTests.cs`) with the app's release-1 model read strictly, as typed
mobile clients do.

| Change in the new release | What last month's app does |
|---|---|
| A new optional field in a response | Ignores it. Safe |
| **A new value in a response enum** (`"Cancelled"`) | **Can't read the response at all**: the whole screen fails, not only the status |
| A field removed or renamed | An error if its model requires the field; if not, a silent blank where the value was |
| **A new required field in a request** | **Every request it sends is rejected** (400) |

A changed type, a narrowed format or a different error shape break it the same way. When in doubt, it's
breaking: the old app can't be fixed after the fact.

## Expand, migrate, contract

1. **Expand.** Add the new field or endpoint next to the old one. Old apps keep working.
2. **Migrate.** Ship the app version that uses it, and wait until the oldest version you support has it.
   The app sends its version (an `App-Version` header, or your equivalent): log it, and you know who still
   calls the old shape.
3. **Contract.** Remove the old one once no supported version reads it, after marking it deprecated.

A new enum value is an expand step too: send it only to app versions that know it (check the app's
version), and map it to an existing value for the others, until the oldest supported app knows it.

## Saying an endpoint is going away

<!-- sample: tests/SkillSamples.Tests/Contracts/Deprecation.cs -->
```csharp
public static class Deprecation
{
    /// <summary>
    /// Marks an endpoint as going away: <c>Deprecation</c> (RFC 9745, a Unix time), <c>Sunset</c> (RFC 8594, an
    /// HTTP date) and a link to what replaces it. Apps and their logs can see it long before it stops working.
    /// </summary>
    public static RouteHandlerBuilder Deprecated(this RouteHandlerBuilder endpoint, DateTimeOffset since, DateTimeOffset sunset, string replacement) =>
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            var headers = context.HttpContext.Response.Headers;
            headers["Deprecation"] = "@" + since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            headers["Sunset"] = sunset.ToString("R", CultureInfo.InvariantCulture);
            headers.Link = $"<{replacement}>; rel=\"deprecation\"";
            return await next(context);
        });
}
```

Use it as `app.MapGet(...).Deprecated(since, sunset, "/v2/visits/{id}")`. Tested: the response carries
`Deprecation: @1788220800`, `Sunset: Mon, 01 Mar 2027 00:00:00 GMT` and the link, the same on an image
without ICU.

## Catching it in CI: oasdiff, set for installed apps

[oasdiff](https://github.com/oasdiff/oasdiff) compares two OpenAPI documents and lists the breaking
changes. Run it on every merge request, comparing the document generated from the branch with the one from
the main branch:

```bash
oasdiff breaking --fail-on ERR --severity-levels oasdiff-levels.txt main.json branch.json
```

**At its defaults it lets the most common app-breaking change through.** It rates a new response enum value
a warning, so `--fail-on ERR` passes it. Tested: the two releases above, as ASP.NET Core generates their
documents, pass at the defaults and fail with this levels file:

<!-- sample: tests/SkillSamples.Tests/Contracts/oasdiff-levels.txt -->
```text
response-property-enum-value-added err
```

Removed response fields and new required request fields are errors already. `--fail-on WARN` also works,
but fails on warnings that don't matter to an app.

To get the documents: generate them at build time (`Microsoft.Extensions.ApiDescription.Server`), or start
the app in a test host and fetch `/openapi/v1.json`, as the tests do. Where the check runs (each service's
pipeline, or wherever the documents are collected) is the team's call.

## Rules
- Every response stays readable by the oldest app version you support; every request it sends stays valid.
- New enum values in responses, removed fields and new required request fields are breaking for apps.
- Expand, migrate, contract. Deprecate with `Deprecation` and `Sunset` before removing.
- oasdiff on every merge request, with the enum rule raised to an error.
