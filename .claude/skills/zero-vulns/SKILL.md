---
name: zero-vulns
description: Zero known-vuln enforcement for .NET: a vulnerable NuGet package (direct or transitive) is a HARD FAIL — NuGetAudit-as-error + dotnet list package --vulnerable.
---

# Zero Vulns — no known-vulnerable packages, enforced

The standard: a service ships with **zero known-vulnerable NuGet packages** — direct **and**
transitive. This is enforcement, not a suggestion. A known-vuln package is a **hard fail** that blocks
merge, the same way a failing test does — not a line in a report someone might read.

Detection already exists in a few places (the read-only check in `/full-review`, `/security-scan`).
This skill is the *enforcement* layer: the gate, the rule, and the remediation.

## The two detection mechanisms (use both)
1. **NuGetAudit in the build** — the build itself fails on vulnerable packages. Set in
   `Directory.Build.props` at the solution root:
   ```xml
   <PropertyGroup>
     <NuGetAudit>true</NuGetAudit>
     <NuGetAuditMode>all</NuGetAuditMode>        <!-- direct AND transitive -->
     <NuGetAuditLevel>low</NuGetAuditLevel>       <!-- surface everything, don't hide lows -->
     <WarningsAsErrors>$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>
   </PropertyGroup>
   ```
   `NU1901–NU1904` are the vulnerability advisories (low → critical). Treating them as **errors** is
   what turns "warned about it" into "the build won't pass." This is the primary gate — it runs on
   every build and in CI automatically.

2. **Explicit scan** for reviews and pre-merge gates:
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```
   Report every hit with package, installed version, severity, advisory URL, and whether it's direct
   or transitive.

## The gate (hard fail)
- **Any** package with a known vulnerability (direct or transitive, any severity) fails the gate.
- The gate is not "0 critical" — it's **0 known vulns**. Low/moderate still fail; a low today is an
  unpatched CVE tomorrow.
- This runs in: the build (NuGetAudit-as-error), `/quality-gate`, `/full-review`, and `/security-scan`.

## Remediation
- **Direct dependency vulnerable** → upgrade to the fixed version (`dotnet add package X --version …`).
- **Transitive dependency vulnerable** (the common, annoying case — you didn't reference it directly) →
  you can't upgrade the parent's choice, so **pin the fixed transitive version directly**:
  ```xml
  <!-- Force the patched version of a transitively-pulled package -->
  <PackageReference Include="System.Vulnerable.Package" Version="8.0.2" />
  ```
  Adding a direct reference to the fixed version overrides the vulnerable transitive one. Document why
  (a comment linking the advisory) so it isn't "cleaned up" later.
- If no fixed version exists yet → this is a real risk decision, not a silent pass: record it
  explicitly (a tracked suppression with an advisory link and an owner), never just ignore the warning.

## Eating our own dog food
The `proven-roslyn` MCP server once tripped `NU1903` on a transitive `Microsoft.Build.Tasks.Core 17.7.2`. Under
this standard that's a real hit, so the fix is to pin the fixed version in the server's `.csproj` (a direct
`PackageReference` to the patched `Microsoft.Build.Tasks.Core`) rather than shipping a tool that fails
the check it enforces.

## Rules
- Zero known vulns — direct and transitive, any severity. A vuln is a hard fail, not a note.
- `NuGetAudit` + `NuGetAuditMode=all` + `NU190x`-as-error in `Directory.Build.props` is the primary gate.
- Transitive vuln → pin the fixed version with a direct `PackageReference` (documented).
- No silent suppression — an un-fixable vuln is a tracked, owned decision with an advisory link.
