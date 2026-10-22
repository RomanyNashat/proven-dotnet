---
name: zero-vulns
description: Zero known-vuln enforcement for .NET: a vulnerable NuGet package (direct or transitive) is a HARD FAIL — NuGetAudit-as-error + dotnet list package --vulnerable, and how to fix one: upgrade the direct package or the parent that brings it, and pin only when no parent release can.
version: 1.2.0
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

## Remediation: the smallest change that removes the vulnerable version

Fix the package you own, not the one you don't. A direct reference added only to override a transitive
version is one more package to keep current, outlives the parent's own fix, and hides the parent from
the next upgrade. So, in order:

1. **Is it a direct dependency?** `dotnet list package` (filter with `findstr <name>` on Windows,
   `grep` elsewhere). If it's there, **upgrade it** to the fixed version. Done.
2. **Otherwise it's transitive.** Find who brings it:
   ```bash
   dotnet list package --include-transitive        # it's listed, with no Requested version
   dotnet nuget why <project.csproj> <package>     # every path from your references down to it
   ```
3. **Upgrade the parent** to a version that brings a fixed one. Ask the feed, rather than guessing from
   release notes:
   ```bash
   python ~/.claude/skills/zero-vulns/scripts/parent_fix.py <parent> <package> --current <your parent version> --framework net10.0
   ```
   It reads which version of the package every release of the parent asks for on your framework (restore
   takes the lowest the range allows, so that's what you get), checks each against the advisories, and
   names the lowest parent version at or above yours that brings a safe one. Exit code 1 means no
   release of the parent does yet. Tested offline, and run against nuget.org in CI.
4. **Only when no parent release fixes it, pin the package directly**, and say why next to the pin, so
   nobody "cleans it up" and nobody forgets to remove it once the parent catches up:
   ```xml
   <!-- GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx. Microsoft.Data.SqlClient 7.1.1, its newest, still
        asks for 9.0.18 (parent_fix.py, 2026-10-23). Remove when a SqlClient release brings >= 9.0.21. -->
   <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
   ```
   That case is real: the newest `Microsoft.Data.SqlClient` asks for `System.Security.Cryptography.Pkcs
   [9.0.18, )` on .NET 8 to 10, and both advisories cover 9.0.18, so a SQL Server service has no parent
   to upgrade. **Under Central Package Management** the pin is one `PackageVersion` in
   `Directory.Packages.props` with `CentralPackageTransitivePinningEnabled`, applied to every project,
   with the same comment (see `msbuild`).
- **Which version is "the fixed version"?** Not the newest one you remember, and not the next major: the
  next major is often flagged by the same advisory. Ask the feed:
  ```bash
  python ~/.claude/skills/zero-vulns/scripts/first_fixed.py System.Security.Cryptography.Pkcs 9.0.11
  # System.Security.Cryptography.Pkcs 9.0.11: GHSA-2fmv-rv2c-59x7; GHSA-846f-f764-76mx
  #   same line: 9.0.21
  #   newer:     9.0.21
  ```
  It reads the advisories restore audits against and the versions the feed publishes, and prints the
  lowest safe version on the same `major.minor` (the smallest change) and the lowest safe one above the
  current version. It exits 1 when no safe version is published yet. Standard library only. On a feed
  that publishes no vulnerability data (a proxy may not), add `--vulnerability-feed
  https://api.nuget.org/v3/index.json`. Then upgrade or pin, and restore with the audit on: the restore
  is the proof. Tested offline in CI, and run against nuget.org there.
  Why it's here: a fix eval run that saw the Pkcs advisory tried `10.0.0`, found that flagged too, and
  gave up; `10.0.13` (or `9.0.21` on the same line) was the fix.
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
- Transitive vuln → upgrade the parent that brings it (`dotnet nuget why`, `parent_fix.py`); pin the package
  directly only when no parent release brings a fixed version, with the reason beside the pin.
- No silent suppression — an un-fixable vuln is a tracked, owned decision with an advisory link.
