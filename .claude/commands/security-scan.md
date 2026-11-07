---
name: security-scan
description: "Security audit against the OWASP Top 10, optionally with healthcare compliance (HIPAA, PDPL)."
allowed-tools: Read, Grep, Glob, Agent
---

## Security Scan

1. **Run security-reviewer agent** (always):
   - OWASP Top 10:2025 checklist against all changed/new code
   - Authentication and authorization validation
   - PII exposure detection in logs, errors, and API responses
   - Cryptographic implementation review
   - Dependency vulnerability check: `dotnet list package --vulnerable`
   - Reference `skills/owasp-aspnetcore/`, `skills/auth-patterns/`, `skills/encryption-patterns/`

2. **Run compliance-auditor agent** (only if healthcare module is active):
   - HIPAA Technical Safeguards (§164.312) compliance
   - Saudi PDPL/SDAIA data residency and consent verification
   - PHI audit trail completeness
   - Reference `skills/healthcare-compliance/`

3. **NuGet vulnerability scan**:
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```

4. Output a consolidated security report with severity levels and specific .NET remediation code.
