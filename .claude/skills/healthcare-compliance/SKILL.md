---
name: healthcare-compliance
description: Healthcare data rules for .NET: PII/PHI handling, audit logging, encryption at rest/in transit, consent, safe errors.
version: 1.0.0
---

# Healthcare Compliance Patterns

> **OPT-IN**: This skill is only needed for healthcare applications or features handling Protected Health Information (PHI) or personal data subject to Saudi PDPL.

## HIPAA Technical Safeguards (§164.312)

### Access Control (§164.312(a)) — Implementation
```csharp
// Role-based access with minimum necessary access principle
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ViewPatientRecords", policy =>
        policy.RequireClaim("permission", "phi:read")
            .AddRequirements(new SameTenantRequirement()))
    .AddPolicy("ModifyPatientRecords", policy =>
        policy.RequireClaim("permission", "phi:write")
            .AddRequirements(new SameTenantRequirement())
            .AddRequirements(new ActiveSessionRequirement()))
    .AddPolicy("ExportPatientData", policy =>
        policy.RequireClaim("permission", "phi:export")
            .RequireRole("DataOfficer", "Admin"));

// Automatic session timeout
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);  // HIPAA recommended
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Emergency access (break-the-glass)
public sealed class EmergencyAccessService(
    IAuditLogger auditLogger,
    IAuthorizationService authService)
{
    public async Task<EmergencyAccessToken> RequestEmergencyAccessAsync(
        int requesterId, string reason, CancellationToken ct)
    {
        var token = new EmergencyAccessToken
        {
            RequesterId = requesterId,          // Id: int identity, assigned on save
            Reason = reason,
            GrantedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            IsRevoked = false
        };

        // ALWAYS log emergency access
        await auditLogger.LogAsync(new AuditEntry
        {
            Action = "EMERGENCY_ACCESS_GRANTED",
            UserId = requesterId,
            Details = $"Emergency access requested. Reason: {reason}",
            Severity = AuditSeverity.Critical
        }, ct);

        return token;
    }
}
```

### Audit Controls (§164.312(b)) — Implementation
```csharp
// Comprehensive PHI access audit trail
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken ct);
    Task LogPhiAccessAsync(PhiAccessEntry entry, CancellationToken ct);
}

public sealed record PhiAccessEntry
{
    public required Guid UserId { get; init; }
    public required string Action { get; init; }        // READ, WRITE, DELETE, EXPORT
    public required string ResourceType { get; init; }  // Patient, MedicalRecord, Prescription
    public required string ResourceId { get; init; }
    public required string IpAddress { get; init; }
    public required string UserAgent { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? Justification { get; init; }
}

public sealed class PhiAuditInterceptor(
    ICurrentUserService currentUser,
    IAuditLogger auditLogger,
    IHttpContextAccessor httpContext) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct)
    {
        var context = eventData.Context!;
        foreach (var entry in context.ChangeTracker.Entries<IPhiEntity>())
        {
            var action = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => null
            };

            if (action is null) continue;

            await auditLogger.LogPhiAccessAsync(new PhiAccessEntry
            {
                UserId = currentUser.UserId,
                Action = action,
                ResourceType = entry.Entity.GetType().Name,
                ResourceId = entry.Property("Id").CurrentValue?.ToString() ?? "unknown",
                IpAddress = httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent = httpContext.HttpContext?.Request.Headers.UserAgent.ToString() ?? "unknown"
            }, ct);
        }

        return await base.SavingChangesAsync(eventData, result, ct);
    }
}

// Audit log storage — immutable, append-only
// PostgreSQL: the app's role gets INSERT and SELECT only (REVOKE UPDATE, DELETE ON audit_log FROM app_role)
// SQL Server: DENY UPDATE, DELETE ON audit_log TO app_role, or (2022+) an append-only ledger table:
//   CREATE TABLE audit_log (...) WITH (LEDGER = ON (APPEND_ONLY = ON))
// Or: Azure Table Storage / Cosmos DB with immutable policy
// Retention: minimum 6 years (HIPAA requirement)
```

### Transmission Security (§164.312(e))
```csharp
// Enforce TLS 1.3 for all PHI transmission
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ConfigureHttpsDefaults(https =>
    {
        https.SslProtocols = SslProtocols.Tls13;  // TLS 1.3 only for PHI
    });
});

// Never include PHI in URLs
// ❌ GET /api/patients?nationalId=1234567890
// ✅ POST /api/patients/search { "nationalId": "1234567890" }  (over HTTPS)
```

## Saudi PDPL/SDAIA Implementation

### Data Residency Middleware
```csharp
// Ensure Saudi personal data stays in Saudi infrastructure
public sealed class DataResidencyMiddleware(
    RequestDelegate next,
    IDataResidencyValidator validator,
    ILogger<DataResidencyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Check if request involves cross-border data transfer
        if (context.Request.Headers.TryGetValue("X-Target-Region", out var targetRegion))
        {
            if (!validator.IsAllowedRegion(targetRegion!))
            {
                logger.LogWarning(
                    "Blocked cross-border data transfer to {Region} for {Path}",
                    targetRegion, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 403,
                    Title = "Data residency violation",
                    Detail = "Personal data cannot be transferred to this region under PDPL"
                });
                return;
            }
        }

        await next(context);
    }
}

public sealed class DataResidencyValidator : IDataResidencyValidator
{
    // Saudi PDPL allows transfer only to countries with adequate protection
    // or with explicit SDAIA approval
    private static readonly HashSet<string> AllowedRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sa-riyadh-1",     // Saudi Arabia
        "sa-jeddah-1",     // Saudi Arabia
        "me-south-1",      // Bahrain (AWS) — with adequacy agreement
        "uae-north",       // UAE (Azure) — with adequacy agreement
    };

    public bool IsAllowedRegion(string region) => AllowedRegions.Contains(region);
}
```

### Consent Tracking
```csharp
public sealed class Consent
{
    public int Id { get; private init; }                 // int identity
    public required int SubjectId { get; init; }       // data subject (patient/user)
    public required string Purpose { get; init; }        // "treatment", "research", "marketing"
    public required ConsentStatus Status { get; set; }
    public required DateTimeOffset GrantedAt { get; init; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public required string ConsentMethod { get; init; }  // "web_form", "paper", "verbal"
    public required string IpAddress { get; init; }
    public string? LegalBasis { get; init; }             // PDPL article reference
}

public enum ConsentStatus { Granted, Withdrawn, Expired }

// Consent verification before processing: a command decorator (see cqrs-eventsourcing). Add
// typeof(ConsentDecorator<,>) to the command chain in AddCqrsHandlers, after validation.
public sealed class ConsentDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IConsentRepository consentRepository,
    ICurrentUserService currentUser)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        if (command is IRequiresConsent requires &&
            !await consentRepository.HasActiveConsentAsync(currentUser.UserId, requires.RequiredPurpose, ct))
        {
            throw new ConsentRequiredException(requires.RequiredPurpose);
        }

        return await inner.HandleAsync(command, ct);
    }
}
```

### Data Subject Rights API
```csharp
// Right to access, correction, deletion, portability
app.MapGroup("/api/data-rights")
    .MapDataRightsEndpoints()
    .RequireAuthorization();

public static class DataRightsEndpoints
{
    public static RouteGroupBuilder MapDataRightsEndpoints(this RouteGroupBuilder group)
    {
        // Right to access — export personal data
        group.MapGet("/export", async (
            ICurrentUserService user, IDataExportService export, CancellationToken ct) =>
        {
            var data = await export.ExportUserDataAsync(user.UserId, ct);
            return TypedResults.File(
                data, "application/json", $"personal-data-{user.UserId}.json");
        });

        // Right to correction
        group.MapPut("/correct", async (
            DataCorrectionRequest request,
            IDataCorrectionService correction,
            ICurrentUserService user,
            CancellationToken ct) =>
        {
            await correction.RequestCorrectionAsync(user.UserId, request, ct);
            return TypedResults.Accepted();
        });

        // Right to deletion (soft-delete with retention period, then hard-delete)
        group.MapDelete("/delete", async (
            IDataDeletionService deletion,
            ICurrentUserService user,
            CancellationToken ct) =>
        {
            await deletion.RequestDeletionAsync(user.UserId, ct);
            return TypedResults.Accepted();
            // Soft-delete immediately, hard-delete after retention period (30 days)
        });

        return group;
    }
}
```

### Breach Notification Readiness
```csharp
public sealed class BreachNotificationService(
    ISdaiaNotificationClient sdaiaClient,
    INotificationService userNotification,
    IAuditLogger auditLogger,
    ILogger<BreachNotificationService> logger)
{
    // Saudi PDPL: 72-hour notification to SDAIA after breach discovery
    public async Task ReportBreachAsync(BreachReport report, CancellationToken ct)
    {
        // 1. Log the breach internally
        await auditLogger.LogAsync(new AuditEntry
        {
            Action = "DATA_BREACH_DETECTED",
            Details = JsonSerializer.Serialize(report),
            Severity = AuditSeverity.Critical
        }, ct);

        // 2. Notify SDAIA within 72 hours
        await sdaiaClient.SubmitBreachNotificationAsync(new SdaiaBreachNotification
        {
            OrganizationName = "Healthcare Platform",
            BreachDate = report.DiscoveredAt,
            DataTypesAffected = report.AffectedDataTypes,
            NumberOfSubjectsAffected = report.EstimatedAffectedCount,
            MitigationActions = report.ImmediateActions,
            ContactPerson = report.DpoContact
        }, ct);

        // 3. Notify affected individuals
        foreach (var subjectId in report.AffectedSubjectIds)
        {
            await userNotification.SendAsync(
                subjectId,
                "Important: Data Security Notification",
                report.UserNotificationTemplate,
                ct);
        }

        logger.LogCritical(
            "Data breach reported to SDAIA. Affected subjects: {Count}",
            report.EstimatedAffectedCount);
    }
}
```

## Compliance Checklist Summary

| Requirement | HIPAA § | PDPL Article | Implementation |
|-------------|---------|--------------|----------------|
| Access control | 164.312(a) | Art. 10 | Policy-based auth, RBAC |
| Audit logging | 164.312(b) | Art. 22 | PhiAuditInterceptor, immutable logs |
| Data integrity | 164.312(c) | Art. 14 | Checksums, encrypted storage |
| Transmission security | 164.312(e) | Art. 15 | TLS 1.3, no PHI in URLs |
| Data residency | N/A | Art. 29 | DataResidencyMiddleware |
| Consent | N/A | Art. 6-7 | ConsentVerificationBehavior |
| Right to access | N/A | Art. 23 | DataRightsEndpoints |
| Right to deletion | N/A | Art. 25 | Soft-delete + hard-delete pipeline |
| Breach notification | Breach Rule | Art. 20 | 72-hour SDAIA notification |
| Encryption at rest | 164.312(a)(2)(iv) | Art. 15 | AES-GCM, envelope encryption |
