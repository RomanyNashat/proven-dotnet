---
name: security-scan
description: "Security audit against the OWASP Top 10, optionally with healthcare compliance (HIPAA, PDPL)."
allowed-tools: Read, Grep, Glob, Agent, Bash(dotnet list package *), Bash(python *config_scan.py *), Bash(python3 *config_scan.py *)
---

## Security Scan

Steps 1 to 3 don't depend on each other: start the agents in one message, so they run at once, and run the
package scan while they work. Then put the three together.

1. **security-reviewer agent** (always):
   - OWASP Top 10:2025 checklist against all changed/new code
   - Authentication and authorization validation
   - PII exposure detection in logs, errors, and API responses
   - Cryptographic implementation review
   - Reference `skills/owasp-aspnetcore/`, `skills/auth-patterns/`, `skills/encryption-patterns/`

2. **compliance-auditor agent** (only if healthcare module is active):
   - HIPAA Technical Safeguards (§164.312) compliance
   - Saudi PDPL/SDAIA data residency and consent verification
   - PHI audit trail completeness
   - Reference `skills/healthcare-compliance/`

3. **NuGet vulnerability scan** (here, not in an agent: the reviewers can't run commands):
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```

4. **The repo's Claude config** (here too, always; `/security-scan config` runs only this step):
   ```bash
   python ~/.claude/skills/owasp-aspnetcore/scripts/config_scan.py --repo .
   ```
   Hooks that download or run encoded code, allow rules that let any command through, an MCP server
   unpinned or with a token in the file, hidden characters or orders to the model in `CLAUDE.md` and the
   `.claude/` files (`owasp-aspnetcore`, section 6). Each high one goes first in the report, with its file
   and line; the fix for an approval in `settings.local.json` is the developer's (remove the rule).

5. Output a consolidated security report with severity levels and specific .NET remediation code.
