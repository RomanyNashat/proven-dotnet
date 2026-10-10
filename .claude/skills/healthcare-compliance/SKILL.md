---
name: healthcare-compliance
description: Patient-data rules in .NET code — an audit of every change to PHI that commits with the change and can't be edited, consent checked for the patient (not the user), break-the-glass access, data residency checked at start-up, safe errors. Saudi PDPL and HIPAA references kept to what's verified. Tested in CI against PostgreSQL.
version: 2.0.0
---

# Healthcare Compliance Patterns

> **Opt-in.** For services that handle protected health information (PHI) or personal data under the
> Saudi PDPL. **Not legal advice:** the legal references here are the ones checked against published
> sources; your DPO or legal team confirms what applies to a service.

## What the law asks of the code (verified references only)

| Requirement | Where it comes from | In the code |
|---|---|---|
| Data subjects' rights (to be informed, access, correction, destruction) | PDPL Article 4 | Endpoints and processes per right; see below |
| Transfer of personal data outside the Kingdom only under conditions | PDPL Article 29, and the Data Transfer Regulations | Data residency checked at start-up |
| Notify the authority (SDAIA) within **72 hours** of becoming aware of a breach that may cause harm; data subjects without undue delay | PDPL Implementing Regulations | An incident runbook, not code (below) |
| Keep the record of processing activities for the processing and **five years** after | PDPL Implementing Regulations | Retention of the audit and processing records |
| Access control, audit controls, integrity, person or entity authentication, transmission security | HIPAA §164.312 (a)–(e) | Policies, the PHI audit, TLS |
| Keep required documentation for **six years** | HIPAA §164.316(b)(2) | Many teams keep audit logs as long |

Article numbers for other duties (consent, health data, security) aren't listed here because they
weren't verified; look them up in the current text rather than trusting a table.

## The PHI audit: in the same save, and append-only

Every change to an entity that holds patient data gets an audit row: who, when, which record, which
columns. The interceptor adds the rows to the same `SaveChanges`, so the change and its audit commit or
roll back together.

<!-- sample: tests/SkillSamples.Tests/Compliance/PhiAuditInterceptor.cs -->
```csharp
// Adds the audit rows to the same SaveChanges, so a change and its audit commit or roll back together:
// no audit row for a change that failed, and no change without one. The record's id has to be known
// before the save, so PHI entities take their keys from a HiLo sequence (`efcore-patterns`).
public sealed class PhiAuditInterceptor(ICurrentUser user, TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditRows(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        AddAuditRows(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void AddAuditRows(DbContext context)
    {
        var at = time.GetUtcNow();
        var rows = context.ChangeTracker.Entries<IPhiRecord>()   // runs DetectChanges first
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new PhiAuditRow
            {
                RecordType = e.Metadata.ClrType.Name,
                RecordId = e.Entity.Id,
                Action = e.State.ToString(),
                // Column names only: the values are patient data, and more people read the audit than the record.
                ChangedColumns = e.State == EntityState.Modified
                    ? string.Join(',', e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name))
                    : "",
                UserId = user.Id,
                At = at
            })
            .ToList();   // before AddRange changes what the tracker holds

        context.AddRange(rows);
    }
}
```

Tested as stories, on PostgreSQL:
- **A doctor records a diagnosis, another revises it an hour later** by setting the property: two rows,
  with the record's real id, each doctor, each time, and `Code` as the changed column. The diagnosis
  code itself is in no audit row.
- **The save fails** (a value too long for its column): the record got its id, and no audit row claims
  the change happened.

Why it's built this way:
- **Same save, not a separate logger call.** An audit written beside the save says a change happened
  when the save then failed, or misses one when the audit call fails after it.
- **HiLo keys for PHI entities.** With identity keys a new record's id is 0 until the insert, and its
  audit row would point at nothing. HiLo gives the id when the record is added (use `AddAsync`, which
  can fetch the next block).
- **Names, not values.** The audit table is read by more people than the record; it mustn't become a
  second copy of the patient data.
- **Reads aren't changes.** HIPAA's audit controls cover access too. Record reads of PHI where the
  service decides to show a record (a query handler), not in the interceptor.

### The service can add audit rows, never change them

<!-- sample: tests/SkillSamples.Tests/Compliance/audit_grants.sql -->
```sql
-- The service's role can add audit rows and read them, never change or remove them.
GRANT SELECT, INSERT, UPDATE, DELETE ON diagnoses, consents TO clinic_app;
GRANT SELECT, INSERT ON phi_audit TO clinic_app;
GRANT USAGE ON SEQUENCE diagnoses_hilo TO clinic_app;
```

Tested as a story: connected as that role, the service saves a diagnosis and its audit row, and then
`UPDATE` and `DELETE` on `phi_audit` fail with `42501 insufficient_privilege`. The grants are part of the
reviewed migration script, and the service never connects as the table owner. On SQL Server:
`DENY UPDATE, DELETE ON phi_audit TO clinic_app`, or an append-only ledger table (2022+).

Column types follow the house rules: bounded strings, `timestamptz`, a `bigint` key for the audit
(`rules/efcore-rules.md`).

## Consent: the patient's, checked before processing

<!-- sample: tests/SkillSamples.Tests/Compliance/ConsentDecorator.cs -->
```csharp
// A command that processes a patient's data for a purpose that needs their consent.
public interface IRequiresConsent
{
    int SubjectId { get; }
    string Purpose { get; }
}

public sealed class ConsentRequiredException(int subjectId, string purpose)
    : Exception($"Subject {subjectId} has no active consent for '{purpose}'.")
{
    public string Purpose => purpose;
}

public sealed class ConsentStore(ComplianceDbContext db)
{
    public Task<bool> HasActiveConsentAsync(int subjectId, string purpose, CancellationToken ct) =>
        db.Consents.AnyAsync(c => c.SubjectId == subjectId && c.Purpose == purpose && c.WithdrawnAt == null, ct);
}

// Checks the consent of the person the data is about. The signed-in user is the doctor or the
// researcher: their own consent says nothing about the patient's data.
public sealed class ConsentDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ConsentStore consents)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        if (command is IRequiresConsent needs && !await consents.HasActiveConsentAsync(needs.SubjectId, needs.Purpose, ct))
            throw new ConsentRequiredException(needs.SubjectId, needs.Purpose);   // → 403 ProblemDetails

        return await inner.HandleAsync(command, ct);
    }
}
```

Register it in the command chain after validation (`cqrs-eventsourcing`). Tested as a story: patient 77
agrees to research use and one export runs; they withdraw, and the next export for them is refused, as
is one for a patient who never agreed. The old version of this skill checked the **signed-in user's**
consent, which is the doctor's: the fix is that the command names the patient.

Consent rows are kept, not deleted, when withdrawn (`WithdrawnAt`): when consent was given and taken
back is part of what has to be shown later.

## Break-the-glass access

<!-- sample: tests/SkillSamples.Tests/Compliance/EmergencyAccessGrant.cs -->
```csharp
// Break-the-glass: a clinician without the usual permission opens one patient's record in an emergency.
// It needs a reason someone can review, covers one patient, and ends on its own.
public sealed record EmergencyAccessGrant(string UserId, int PatientId, string Reason, DateTimeOffset ExpiresAt)
{
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(30);

    public static EmergencyAccessGrant Open(string userId, int patientId, string reason, TimeProvider time) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10
            ? throw new ArgumentException("Emergency access needs a reason someone can review later.", nameof(reason))
            : new(userId, patientId, reason.Trim(), time.GetUtcNow() + Lasts);

    public bool Allows(int patientId, TimeProvider time) => patientId == PatientId && time.GetUtcNow() < ExpiresAt;
}
```

Tested as a story: "urgent" isn't a reason; a real one opens patient 5, not patient 6, and nothing after
30 minutes. Store every grant and audit the reads made under it; someone reviews them the next day.

## Data residency: checked where configuration is read

<!-- sample: tests/SkillSamples.Tests/Compliance/DataResidencyOptions.cs -->
```csharp
// Where personal data is stored is decided by configuration, so check it where configuration is read:
// a storage region outside the allowed list stops the service at start-up.
public sealed class DataResidencyOptions
{
    public const string Section = "DataResidency";

    public string StorageRegion { get; init; } = "";
    public string[] AllowedRegions { get; init; } = [];
}

public static class DataResidencySetup
{
    public static IServiceCollection AddDataResidencyCheck(this IServiceCollection services)
    {
        services.AddOptions<DataResidencyOptions>()
            .BindConfiguration(DataResidencyOptions.Section)
            .Validate(o => o.AllowedRegions.Contains(o.StorageRegion, StringComparer.OrdinalIgnoreCase),
                "DataResidency:StorageRegion must be one of DataResidency:AllowedRegions.")
            .ValidateOnStart();
        return services;
    }
}
```

Tested as a story: storage pointed at `eu-west-1` stops the service at start-up. The old version of this
skill blocked transfers in a middleware that read the target region from a **request header**: a client
that leaves the header out passes. Residency is where the data is stored and where it's sent, so it's
checked in configuration and in the clients that send data out, not in a header.

## Other safeguards

- **Access:** policies on permissions, not roles (`auth-patterns`); every lookup by id checks the caller
  may see that record (`rules/security.md`).
- **No PHI in URLs or logs.** A national id in a query string ends up in proxy and ingress logs: send it
  in the body over HTTPS. Logs mask personal data (`pii-masking`).
- **Errors:** `ProblemDetails` without internal detail (`api-design`).
- **Encryption at rest** for the most sensitive fields: `encryption-patterns`.
- **TLS:** where TLS ends at the ingress, the service-to-service hop needs its own (`rules/security.md`).
- **Breach readiness is a runbook, not code:** who decides it's a notifiable breach, who notifies SDAIA
  (72 hours from becoming aware), how affected people are told, and where the audit rows are that show
  what was accessed. Practise it once.

## Rules
- PHI changes audited in the same save, with column names and never values; HiLo keys on PHI entities.
- The service's role can't update or delete the audit.
- Consent checked for the data subject named in the command; withdrawals kept.
- Break-the-glass: a reason, one patient, a time limit, reviewed.
- Data residency validated at start-up from configuration.
- Legal references only when verified; the DPO confirms.
