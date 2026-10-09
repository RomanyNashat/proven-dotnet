# ASP.NET Core Rules

> Reference material for day-to-day work moved to the `api-design` skill (loaded on demand).
> What stays here is the non-negotiable part: hard bans and anti-patterns.


## Middleware Order (critical — order matters)
```
1. ExceptionHandler (global error handling)
2. HSTS
3. HttpsRedirection
4. Static Files
5. Routing
6. CORS
7. Authentication
8. Authorization
9. Rate Limiting
10. Response Compression
11. Custom middleware
12. Endpoints
```


## Anti-Patterns (NEVER)
- No business logic in controllers or endpoint handlers — delegate to Application layer.
- No `HttpContext` access in business/domain layers — pass needed values as parameters.
- No `IActionResult` in Minimal APIs — use `TypedResults` for type safety. (Controllers use `ActionResult<T>`.)
- No synchronous I/O in the request pipeline — all I/O must be async.
- No hardcoded URLs — use configuration, service discovery, or named HTTP clients.
- No conventional routing for APIs — always use attribute routing (`[Route]`) for controllers.
- No `Controller` suffix on API controllers — the `[controller]` token in the route template handles it.
