---
name: msbuild
description: Solution-wide build settings and package versions — Directory.Build.props, Central Package Management (Directory.Packages.props, transitive pinning, version ranges), moving a solution onto it, and finding out why the build or restore did what it did (-getProperty, dotnet nuget why, binary logs). Restored in CI with the real CLI.
version: 1.3.0
---

# MSBuild and package versions

Two files at the solution root decide most of how every project builds:

- **`Directory.Build.props`**: settings every project shares (target framework, nullable, warnings as
  errors, the package audit).
- **`Directory.Packages.props`**: one version for each package, for the whole solution.

The samples below are one small solution, restored in CI against nuget.org: a data project that uses
SQL Server and a test project.

## 1. Shared settings: `Directory.Build.props`

<!-- sample: tests/SkillSamples.Tests/MsBuild/Directory.Build.props -->
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Every project's bin and obj under artifacts/<project>/, so no two projects can share a folder.
         The path is set here because the default follows the nearest Directory.Build.props, and the
         nested one under tests/ would move the test projects to tests/artifacts. -->
    <UseArtifactsOutput>true</UseArtifactsOutput>
    <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
    <!-- zero-vulns: direct and transitive packages are audited, and a known vulnerability fails the restore. -->
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <WarningsAsErrors>$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

**MSBuild imports only the nearest `Directory.Build.props`.** A second one lower down (say in `tests/`,
to make test projects non-packable) replaces the root one unless it imports it. Nothing warns you: the
test projects just lose nullable, warnings as errors and the audit (tested). So a nested file always
starts with the import:

<!-- sample: tests/SkillSamples.Tests/MsBuild/tests/Directory.Build.props -->
```xml
<Project>
  <!-- MSBuild imports only the nearest Directory.Build.props, so this one brings in the root's first. -->
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
```

**Outputs: `UseArtifactsOutput`, never a shared `obj`.** "Tidying" outputs by pointing every project's
`BaseIntermediateOutputPath` or `BaseOutputPath` at one folder makes the projects share it (tested):
one `project.assets.json` for all of them, so each restore overwrites the last project's, and builds
overwrite each other's files. `UseArtifactsOutput` gives each project its own
`artifacts/bin/<project>/` and `artifacts/obj/<project>/` (tested). Set `ArtifactsPath` with it: the
default sits next to the *nearest* `Directory.Build.props`, so a nested one under `tests/` moves the
test projects to `tests/artifacts/` (tested).

What goes where:
- **`Directory.Build.props`** is read *before* the project file: defaults the project can still change.
- **`Directory.Build.targets`** is read *after* it: anything that depends on what the project set, such
  as a condition on `$(TargetFramework)` or `$(IsTestProject)`.
- No `PackageReference` in either file. Every project would get the package, including ones that must
  not. For an analyzer that really belongs everywhere, use `GlobalPackageReference` in
  `Directory.Packages.props`: it is added to every project as a development-only dependency.

## 2. One version per package: Central Package Management

<!-- sample: tests/SkillSamples.Tests/MsBuild/Directory.Packages.props -->
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- A version listed here also applies when the package only arrives through another package. -->
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    <!-- One version per package for the whole solution: a project can't quietly take its own. -->
    <CentralPackageVersionOverrideEnabled>false</CentralPackageVersionOverrideEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="6.1.6" />
  </ItemGroup>

  <ItemGroup Label="Tests">
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <!-- 8.0 and later need a paid licence for commercial use. NuGet restores the lowest version in a
         range, so this gives 7.0.0: raise the lower bound to take a 7.x fix. -->
    <PackageVersion Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
  </ItemGroup>

  <ItemGroup Label="Security pins">
    <!-- Arrives through Microsoft.Data.SqlClient at 9.0.11. CVE-2026-70162 (GHSA-2fmv-rv2c-59x7, moderate)
         and CVE-2026-84008 (GHSA-846f-f764-76mx, high), both denial of service, are fixed in 10.0.13.
         Remove the pin once SqlClient depends on a fixed version. -->
    <PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
  </ItemGroup>
</Project>
```

The projects then name packages without versions:

<!-- sample: tests/SkillSamples.Tests/MsBuild/src/Clinic.Data/Clinic.Data.csproj -->
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" />
  </ItemGroup>
</Project>
```

What each setting buys you (all tested):

| Setting | Without it | With it |
|---|---|---|
| `CentralPackageTransitivePinningEnabled` | Pkcs arrives through SqlClient at 9.0.11, the audit fails the restore, and the fix is a direct reference in every project that uses SqlClient | one `PackageVersion` line lifts it to 10.0.13 in every project; `dotnet nuget why` still shows it came from SqlClient |
| `CentralPackageVersionOverrideEnabled=false` | a project can write `VersionOverride="8.0.0"` and take FluentAssertions 8 on its own | the restore fails with **NU1013** |
| (always on with CPM) | — | a project that writes `Version="..."` on a `PackageReference` fails with **NU1008** |

**Version ranges.** NuGet restores the *lowest* version a range allows, not the newest. `[7.0.0,8.0.0)`
keeps FluentAssertions below 8, and it restores 7.0.0 until someone raises the lower bound. That is
the point of a range here: a fence, not a way to float.

**Floating versions (`10.*`) are refused** under CPM (**NU1011**). Pick exact versions. Restores become
repeatable, and Dependabot or Renovate proposes each bump as a change you review. (A test suite that
exists to catch what the newest patch breaks can set `CentralPackageFloatingVersionsEnabled`, as this
repo's own tests do; a service doesn't.)

**One feed per package.** With CPM and more than one feed, NuGet warns (**NU1507**) until every package
is mapped to a source. Map them: it also stops a same-named package on a public feed from replacing an
internal one.

```xml
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="*" />
  </packageSource>
</packageSourceMapping>
```

## 3. Moving a solution onto Central Package Management

One merge request, with the tests green before and after.

1. **List what you have**, twice:
   ```
   dotnet list package --include-transitive --format json > before.json
   python ~/.claude/skills/msbuild/scripts/cpm_check.py snapshot . > versions-before.json
   ```
   The first is what restores. The second is what the files say, each version as written:
   `dotnet list package` shows the version a range resolved to, not the range.
2. **Pick one version per package.** Where projects disagree, take the highest and treat it as an upgrade
   of the other projects: their tests are what prove it.
3. **Create `Directory.Packages.props`** at the solution root, with a `PackageVersion` for each package.
   Floating versions become exact ones. Ranges stay exactly as written.
4. **Remove `Version` from every `PackageReference`.** The restore reports any you missed (NU1008).
5. **Move security pins.** A direct `PackageReference` that only existed to lift a transitive package
   becomes a `PackageVersion` under transitive pinning, with its advisory comment. Delete the direct
   references.
6. **Compare.**
   ```
   python ~/.claude/skills/msbuild/scripts/cpm_check.py compare versions-before.json .
   ```
   It fails on a package with no central version and on a range that didn't survive as written, and
   lists every project whose version moved. A conversion done by a tool is where this bites: one that
   only reads exact versions drops `[7.0.0,8.0.0)`, the restore fails, and the obvious fix is the newest
   version, 8.x, the paid licence. Then restore, build and test, list again into `after.json`, and check
   every difference from `before.json` is one you chose in step 2 or 5.

## 4. Why did the build do that?

Start with the cheapest question that answers it.

| Question | Command |
|---|---|
| What value does a property end up with? | `dotnet msbuild <project> -getProperty:Nullable -getProperty:TreatWarningsAsErrors` |
| Which files were imported, in what order? | `dotnet msbuild <project> -pp:full.xml` (the project with every import inlined) |
| Why do I have this version of a package? | `dotnet nuget why <project> <package>` (after a restore) |
| Where was a property set, why was a target skipped, what went to the compiler? | a binary log (below) |

`-getProperty` evaluates the project without building it. With one name it prints the value; with several
it prints JSON (`{"Properties": {...}}`).

### Binary logs

A binary log records the whole build: every import, every property assignment and where it happened,
every target and why it ran or was skipped, and the exact compiler command line.

```bash
dotnet build -bl:build.binlog
dotnet restore -bl:restore.binlog
```

It is a binary file. To read it without a viewer, replay it into a text log and search that:

```bash
dotnet msbuild build.binlog -noconlog "-flp:logfile=build.log;verbosity=diagnostic"
```

(The quotes matter in PowerShell, where `;` ends a command.) In the text log:
- `Property reassignment: $(Nullable)=...` gives the old value, the new one, and the file and line that
  changed it.
- `Target "..." skipped, due to false condition` gives the condition that was false.
- The `Csc` task's command line shows the references, defines and warnings the compiler really got.

**A binary log can hold secrets.** It records property values and environment variables: feed tokens,
connection strings, anything a pipeline put in the environment. Treat it like a production log. Don't
attach it to a public issue or a chat, and in CI upload it only on failure, as a short-lived private
artifact.

## Rules

- Shared settings in `Directory.Build.props`; a nested one imports its parent first.
- Outputs through `UseArtifactsOutput` with an explicit `ArtifactsPath`; never one `obj` or `bin` for
  several projects.
- No `PackageReference` in `Directory.Build.props` or `.targets`. `GlobalPackageReference` for analyzers.
- With CPM: no `Version` on `PackageReference`, `VersionOverride` off, no floating versions.
- A transitive security pin is a `PackageVersion` under transitive pinning, with the advisory in a
  comment and when to remove it (`zero-vulns`).
- A range is a fence: NuGet restores its lowest version.
- Diagnose from the cheapest question up: `-getProperty`, `-pp`, `dotnet nuget why`, then a binary log.
  Binary logs stay private.

## See also

- **`zero-vulns`**: the audit gate this solution turns on, and what to do when it fails.
- **`dotnet-upgrade`**: moving the target framework, which usually moves the package versions with it.
