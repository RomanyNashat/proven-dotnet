#!/usr/bin/env python3
"""Scans a .NET service for the traps this harness's own tests found in real libraries.

    python scan_traps.py <service folder> [<more folders>] [--json] [--fail-on critical|warning]

Read-only. Each finding names the file and line, why it bites in production, the fix, and the skill whose
test proves it. A text scan points at where to look; it can't prove a bug the way a test does, so a finding
is a place to read, and a clean result isn't a proof. Exit code 1 with --fail-on when a finding at that
severity or worse exists.
"""
import argparse
import json
import pathlib
import re
import sys
from dataclasses import asdict, dataclass

SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".vs", ".idea", "artifacts", "TestResults"}
TEST_PATH = re.compile(r"(^|/)(tests?|[^/]*\.Tests?)(/|$)", re.IGNORECASE)
SEVERITIES = ("critical", "warning", "info")
COMMENT = re.compile(r"^\s*(//|\*|/\*|--|#)")
CHECKS = []


@dataclass
class Finding:
    check: str
    severity: str
    path: str
    line: int
    title: str
    fix: str
    skill: str


def check(check_id, severity, title, fix, skill):
    def register(func):
        CHECKS.append((check_id, severity, title, fix, skill, func))
        return func
    return register


class Repo:
    def __init__(self, root):
        self.root = root
        self.files = {}
        for path in sorted(root.rglob("*")):
            rel = path.relative_to(root).as_posix()
            if path.is_file() and not SKIP_DIRS.intersection(rel.split("/")[:-1]) and self.wanted(path.name):
                self.files[rel] = path.read_text(encoding="utf-8-sig", errors="replace")

    @staticmethod
    def wanted(name):
        lower = name.lower()
        return (lower.endswith((".cs", ".csproj", ".props", ".targets", ".sql", ".yml", ".yaml"))
                or lower.startswith("dockerfile") or (lower.startswith("appsettings") and lower.endswith(".json")))

    def of(self, *suffixes, tests=False):
        for rel, text in self.files.items():
            if rel.lower().endswith(suffixes) and (tests or not TEST_PATH.search(rel)):
                yield rel, text

    def has(self, pattern, *suffixes, tests=False):
        return any(re.search(pattern, text) for _, text in self.of(*suffixes, tests=tests))

    def packages(self, tests=False):
        """{package name: [(rel, version text, line)]} from PackageReference and PackageVersion."""
        found = {}
        for rel, text in self.of(".csproj", ".props", ".targets", tests=tests):
            for tag in re.finditer(r"<Package(?:Reference|Version)\b[^>]*>", text):
                attrs = dict(re.findall(r'(\w+)="([^"]*)"', tag[0]))
                if "Include" in attrs:
                    found.setdefault(attrs["Include"], []).append((rel, attrs.get("Version", ""), line_of(text, tag.start())))
        return found


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def matches(text, pattern, flags=0):
    """Matches outside comment lines, as (line number, match)."""
    for match in re.finditer(pattern, text, flags):
        start = text.rfind("\n", 0, match.start()) + 1
        end = text.find("\n", match.start())
        if not COMMENT.match(text[start:end if end != -1 else len(text)]):
            yield line_of(text, match.start()), match


def call_text(text, index):
    """The text of the call whose '(' comes first at or after index, up to its matching ')'."""
    start = text.find("(", index)
    depth = 0
    for i in range(start, len(text)):
        depth += {"(": 1, ")": -1}.get(text[i], 0)
        if depth == 0:
            return text[index:i + 1]
    return text[index:]


def statement(text, index):
    end = text.find(";", index)
    return text[index:end if end != -1 else len(text)]


def version(text):
    numbers = re.findall(r"\d+", text)
    return tuple(int(n) for n in numbers[:3]) if numbers else ()


def uses_sqlclient(repo):
    names = repo.packages()
    return any(n in names for n in ("Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore.SqlServer"))


# --- Data and EF Core -------------------------------------------------------------------------------

@check("app-changes-schema", "critical", "The app migrates or creates the schema itself",
       "Remove it; apply reviewed up/down scripts from the pipeline. In tests, apply the same script.",
       "efcore-patterns")
def app_changes_schema(repo):
    for rel, text in repo.of(".cs", tests=True):
        for line, _ in matches(text, r"\.Database\.(Migrate|MigrateAsync|EnsureCreated|EnsureCreatedAsync)\s*\("):
            yield rel, line, ("warning" if TEST_PATH.search(rel) else None)


@check("sqlclient-invariant-mode", "critical", "Globalization-invariant mode on a service that uses SqlClient",
       "SqlClient refuses to connect in invariant mode: give the image ICU (icu-libs) and turn invariant mode off.",
       "sqlserver-patterns")
def sqlclient_invariant(repo):
    if not uses_sqlclient(repo):
        return
    pattern = r"DOTNET_SYSTEM_GLOBALIZATION_INVARIANT\W{1,4}(true|1)\b|<InvariantGlobalization>\s*true"
    for rel, text in repo.of(".csproj", ".props", ".yml", ".yaml", ".json", "", tests=False):
        if rel.lower().endswith(".cs"):
            continue
        for line, _ in matches(text, pattern, re.IGNORECASE):
            yield rel, line, None


@check("sqlclient-pkcs-unpinned", "warning", "SqlClient without a direct pin of System.Security.Cryptography.Pkcs",
       "SqlClient brings in Pkcs 9.0.x (CVE-2026-70162, CVE-2026-84008). Confirm with "
       "`dotnet list package --vulnerable --include-transitive` and pin 10.0.13 or later directly.",
       "msbuild")
def sqlclient_pkcs(repo):
    names = repo.packages()
    if not uses_sqlclient(repo):
        return
    if any(version(v) >= (10, 0, 13) for _, v, _ in names.get("System.Security.Cryptography.Pkcs", [])):
        return
    for name in ("Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore.SqlServer"):
        for rel, _, line in names.get(name, []):
            yield rel, line, None


@check("merge-without-holdlock", "warning", "MERGE without HOLDLOCK",
       "Two concurrent MERGEs both see no row and both insert: add WITH (HOLDLOCK), or UPDATE then INSERT under "
       "UPDLOCK, HOLDLOCK.", "dapper-patterns")
def merge_without_holdlock(repo):
    for rel, text in repo.of(".sql", ".cs"):
        for line, match in matches(text, r"\bMERGE\s+(INTO\s+)?[\[\w]"):
            if "HOLDLOCK" not in text[match.start():match.start() + 120].upper():
                yield rel, line, None


@check("online-index-op", "info", "ONLINE = ON in a script",
       "Online index operations are Enterprise-only: on Standard edition the script fails. Check the edition.",
       "sqlserver-patterns")
def online_index(repo):
    for rel, text in repo.of(".sql", ".cs"):
        for line, _ in matches(text, r"ONLINE\s*=\s*ON", re.IGNORECASE):
            yield rel, line, None


@check("arabic-text-not-unicode", "warning", "Arabic text in a non-Unicode column",
       "varchar turns Arabic into '?': keep Arabic columns Unicode (nvarchar on SQL Server).", "localization")
def arabic_not_unicode(repo):
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"\.Property\(\s*\w+\s*=>\s*\w+\.\w*Ar\s*\)"):
            if re.search(r"IsUnicode\(\s*false\s*\)|HasColumnType\(\s*\"varchar", statement(text, match.start())):
                yield rel, line, None
    for rel, text in repo.of(".sql"):
        for line, _ in matches(text, r"\b\w*(_ar|Ar)\b\]?\s+(var)?char\s*\(", re.IGNORECASE):
            yield rel, line, None


# --- Dependency injection and options -------------------------------------------------------------

@check("singleton-holds-dbcontext", "critical", "A singleton that takes a DbContext",
       "One context for every request and run (Production doesn't validate scopes, so it starts). Make it scoped, "
       "or take IServiceScopeFactory.", "dotnet-core")
def singleton_dbcontext(repo):
    classes = {}
    for rel, text in repo.of(".cs"):
        for match in re.finditer(r"\bclass\s+(\w+)\s*(\([^)]*\))?", text):
            ctor = match[2] or ""
            explicit = re.search(rf"\b{match[1]}\s*\(([^)]*)\)\s*(:|\{{)", text)
            classes[match[1]] = ctor + (explicit[1] if explicit else "")
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"AddSingleton<\s*(?:\w+\s*,\s*)?(\w+)\s*>"):
            if re.search(r"\w*DbContext\b", classes.get(match[1], "")):
                yield rel, line, None


@check("scopes-not-validated", "info", "Scope validation isn't on in every environment",
       "Set ValidateScopes and ValidateOnBuild for all environments; Production skips them by default.",
       "dotnet-core")
def scopes_not_validated(repo):
    if repo.has(r"ValidateScopes", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"\b(WebApplication\.CreateBuilder|Host\.CreateApplicationBuilder|Host\.CreateDefaultBuilder)\s*\("):
            yield rel, line, None
            return


@check("options-not-validated-on-start", "info", "Options validated late or not at all",
       "Add .ValidateOnStart() so bad configuration stops the start, not the first request.", "dotnet-core")
def options_on_start(repo):
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"\.AddOptions<\w+>\s*\("):
            if "ValidateOnStart" not in statement(text, match.start()):
                yield rel, line, None


@check("base-address-loses-path", "warning", "A base address with a path but no trailing slash",
       "new Uri(\"https://host/api/v2\") + \"items\" goes to /api/items: end the base address with '/'.",
       "dotnet-core")
def base_address(repo):
    with_path = r"https?://[^/\"\s]+/[^\"\s]*[^/\"\s]"
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, rf"BaseAddress\s*=\s*new\s+Uri\(\s*\"{with_path}\""):
            yield rel, line, None
    for rel, text in repo.of(".json"):
        for line, _ in matches(text, rf"\"\w*(BaseAddress|BaseUrl|BaseUri)\"\s*:\s*\"{with_path}\"", re.IGNORECASE):
            yield rel, line, None


@check("serilog-factory-with-redaction", "warning", "Serilog as the logger factory, with redaction enabled",
       "Redaction runs in the factory EnableRedaction installs. UseSerilog() before it: nothing logged through "
       "ILogger reaches Serilog. After it: classified fields vanish. Add Serilog as a provider instead: "
       "builder.Logging.AddSerilog(logger, dispose: true).", "pii-masking")
def serilog_factory(repo):
    if not repo.has(r"EnableRedaction\s*\(", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"(\bUseSerilog|Services\s*\.\s*AddSerilog)\s*\("):
            yield rel, line, None


# --- Time and culture -----------------------------------------------------------------------------

@check("ar-sa-default-calendar", "warning", "ar-SA used with its default calendar",
       "Its calendar comes from the machine's ICU (Hijri on one image, Gregorian on another): clone it and set "
       "DateTimeFormat.Calendar explicitly.", "localization")
def ar_sa(repo):
    for rel, text in repo.of(".cs"):
        if re.search(r"Calendar\s*=", text):
            continue
        for line, match in matches(text, r"(new\s+CultureInfo|GetCultureInfo|CreateSpecificCulture)\(\s*\"ar-SA\""):
            # A start-up check that the culture exists formats nothing: `_ = GetCultureInfo("ar-SA", predefinedOnly: true)`.
            before = text[text.rfind("\n", 0, match.start()) + 1:match.start()]
            if re.search(r"\b_\s*=\s*$", before) or "predefinedOnly" in call_text(text, match.start()):
                continue
            yield rel, line, None


@check("timezone-without-fallback", "warning", "A time-zone lookup with no fallback",
       "Throws TimeZoneNotFoundException on an image without tzdata: add tzdata, and fall back to a fixed offset.",
       "localization")
def timezone_fallback(repo):
    for rel, text in repo.of(".cs"):
        if re.search(r"TimeZoneNotFoundException|TryFindSystemTimeZoneById", text):
            continue
        for line, _ in matches(text, r"FindSystemTimeZoneById\s*\("):
            yield rel, line, None


@check("windows-timezone-id", "warning", "A Windows time-zone id",
       "On Linux it only resolves through ICU: an image with tzdata but no ICU throws. Use the IANA id (Asia/Riyadh).",
       "localization")
def windows_timezone(repo):
    for rel, text in repo.of(".cs", ".json"):
        for line, _ in matches(text, r"\"[A-Z][A-Za-z.\- ]+ Standard Time\""):
            yield rel, line, None


# --- Scheduling ---------------------------------------------------------------------------------

@check("quartz-missed-run-dropped", "critical", "Quartz persistent store without ScheduleTriggerRelativeToReplacedTrigger",
       "Each start replaces the trigger and drops a run missed while the service was down: set "
       "options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger = true.", "quartz-scheduling")
def quartz_missed_run(repo):
    if repo.has(r"ScheduleTriggerRelativeToReplacedTrigger\s*=\s*true", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"\.UsePersistentStore\s*\("):
            yield rel, line, None


@check("quartz-cron-in-utc", "warning", "A cron trigger with no time zone",
       "It fires in the pod's zone (UTC): add .InTimeZone(...).", "quartz-scheduling")
def quartz_cron_utc(repo):
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"(WithCronSchedule|CronScheduleBuilder\.\w+)\s*\("):
            if "InTimeZone" not in call_text(text, match.start()) + statement(text, match.start()):
                yield rel, line, None


@check("quartz-script-drops-tables", "critical", "Quartz schema script with DropDb = 1",
       "The shipped script drops every Quartz table first: set DropDb to 0 before it goes to the pipeline.",
       "quartz-scheduling")
def quartz_dropdb(repo):
    for rel, text in repo.of(".sql"):
        for line, _ in matches(text, r"DropDb\b[^;\n]*?(:=|=)\s*1\b", re.IGNORECASE):
            yield rel, line, None


@check("quartz-di-job-factory", "info", "UseMicrosoftDependencyInjectionJobFactory()",
       "Constructor injection is the default in Quartz 3.3+: remove the call.", "quartz-scheduling")
def quartz_job_factory(repo):
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"UseMicrosoftDependencyInjectionJobFactory\s*\("):
            yield rel, line, None


@check("hangfire-filter-on-class", "warning", "[AutomaticRetry] or [Queue] on a class that implements an interface",
       "Hangfire reads filters from the type the job was enqueued through: put them on the interface method.",
       "hangfire-patterns")
def hangfire_filter(repo):
    declaration = r"\b(class|interface|record)\s+\w+[^{;]*"
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"^\s*\[(AutomaticRetry|Queue)\b", re.MULTILINE):
            rest = text[match.end():].split("\n")
            target = next((l for l in rest[1:] if l.strip() and not l.strip().startswith("[")), "")
            owner = re.search(declaration, target) or (list(re.finditer(declaration, text[:match.start()])) or [None])[-1]
            if owner and owner[1] == "class" and re.search(r":\s*[^{]*\bI[A-Z]\w*", owner[0]):
                yield rel, line, None


@check("hangfire-schema-at-boot", "warning", "Hangfire creates its schema at start",
       "PrepareSchemaIfNecessary is true by default: set it to false and apply Hangfire's script from the pipeline.",
       "hangfire-patterns")
def hangfire_schema(repo):
    if repo.has(r"PrepareSchemaIfNecessary\s*=\s*false", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"\.Use(SqlServer|PostgreSql)Storage\s*\("):
            yield rel, line, None


@check("hangfire-dashboard-default-filter", "warning", "Hangfire dashboard with its default filter",
       "The default allows local requests only, which in a pod behind an ingress means nobody: set "
       "Authorization = [] and an authorization policy.", "hangfire-patterns")
def hangfire_dashboard(repo):
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"\b(UseHangfireDashboard|MapHangfireDashboard)\s*\("):
            if "Authorization" not in call_text(text, match.start()):
                yield rel, line, None


# --- Messaging ----------------------------------------------------------------------------------

@check("rabbitmq-quorum-without-dead-letter", "warning", "A quorum queue with no dead-letter exchange",
       "RabbitMQ 4 drops a message after its delivery limit (20 by default): give the queue a dead-letter exchange.",
       "rabbitmq-patterns")
def rabbit_dlx(repo):
    for rel, text in repo.of(".cs"):
        if "x-dead-letter-exchange" in text:
            continue
        for line, _ in matches(text, r"\"x-queue-type\"\s*\]?\s*[,=]\s*\"quorum\""):
            yield rel, line, None


@check("cap-publish-confirms-off", "warning", "CAP on RabbitMQ without publisher confirms",
       "A message CAP marked as sent can be lost by the broker: set PublishConfirms = true.", "cap-library")
def cap_confirms(repo):
    if not repo.has(r"\.AddCap\s*\(", ".cs") or repo.has(r"PublishConfirms\s*=\s*true", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"\.UseRabbitMQ\s*\("):
            yield rel, line, None


@check("cap-default-retries", "info", "CAP with its default 50 retries",
       "50 retries keep a broken message going for most of an hour: set FailedRetryCount.", "cap-library")
def cap_retries(repo):
    if repo.has(r"FailedRetryCount", ".cs"):
        return
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"\.AddCap\s*\("):
            yield rel, line, None


@check("grpc-http-resilience", "warning", "AddStandardResilienceHandler() as a gRPC client's retry",
       "gRPC errors come back as HTTP 200, so it never retries them: use the channel's service-config retry.",
       "grpc-patterns")
def grpc_resilience(repo):
    for rel, text in repo.of(".cs"):
        for line, match in matches(text, r"\.AddGrpcClient<"):
            if "AddStandardResilienceHandler" in statement(text, match.start()):
                yield rel, line, None


@check("signalr-client-picks-group", "critical", "A hub method that joins the group the client names",
       "Any client can join any patient's group: derive the group from the caller's identity and check access.",
       "signalr")
def signalr_group(repo):
    for rel, text in repo.of(".cs"):
        if not re.search(r":\s*Hub\b", text):
            continue
        params = set(re.findall(r"\b(?:string|int|long|Guid)\s+(\w+)\s*[,)]", text))
        for line, match in matches(text, r"AddToGroupAsync\(\s*Context\.ConnectionId\s*,"):
            group = call_text(text, match.start())[len(match[0]):]   # the group name: a parameter, or a string built from one
            if any(re.search(rf"\b{re.escape(p)}\b", group) for p in params):
                yield rel, line, None


# --- Security -----------------------------------------------------------------------------------

@check("cors-any-origin", "critical", "SetIsOriginAllowed(_ => true)",
       "Answers every origin with itself, credentials included: list the origins.", "owasp-aspnetcore")
def cors_any(repo):
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"SetIsOriginAllowed\(\s*\w+\s*=>\s*true\s*\)"):
            yield rel, line, None


@check("jwt-audience-not-validated", "warning", "ValidateAudience = false",
       "A token issued for another service is accepted here: validate the audience.", "auth-patterns")
def jwt_audience(repo):
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"ValidateAudience\s*=\s*false"):
            yield rel, line, None


@check("keycloak-roles-unmapped", "warning", "RoleClaimType pointed at Keycloak's JSON role claim",
       "Keycloak's roles sit inside JSON claims, so RequireRole never sees them: map them out of realm_access.",
       "auth-patterns")
def keycloak_roles(repo):
    for rel, text in repo.of(".cs"):
        for line, _ in matches(text, r"RoleClaimType\s*=\s*\"(realm_access\.roles|roles)\""):
            yield rel, line, None


@check("openiddict6-on-efcore10", "critical", "OpenIddict 6 on EF Core 10",
       "Revoking tokens in bulk (logout) throws TypeLoadException: move to OpenIddict 7, or call "
       ".DisableBulkOperations() until then.", "openiddict-server")
def openiddict6(repo):
    names = repo.packages()
    if repo.has(r"DisableBulkOperations\s*\(", ".cs"):
        return
    efcore10 = any(version(v)[:1] == (10,) for n, refs in names.items() if n.startswith("Microsoft.EntityFrameworkCore")
                   for _, v, _ in refs)
    for name, refs in names.items():
        if name.startswith("OpenIddict") and efcore10:
            for rel, v, line in refs:
                if version(v)[:1] == (6,):
                    yield rel, line, None


@check("fluentassertions-licence", "critical", "FluentAssertions 8 or later, or 7 without a range",
       "8.0+ needs a paid licence for commercial use: Version=\"[7.0.0,8.0.0)\".", "testing-tdd")
def fluentassertions(repo):
    for rel, v, line in repo.packages(tests=True).get("FluentAssertions", []):
        if not v:
            continue
        if version(v) >= (8,):
            yield rel, line, None
        elif not v.startswith("["):
            yield rel, line, "warning"


def scan(root):
    repo = Repo(pathlib.Path(root))
    findings = []
    for check_id, severity, title, fix, skill, func in CHECKS:
        for rel, line, override in func(repo) or []:
            findings.append(Finding(check_id, override or severity, rel, line, title, fix, skill))
    return sorted(findings, key=lambda f: (SEVERITIES.index(f.severity), f.path, f.line))


def report(root, findings):
    lines = [f"## {pathlib.Path(root).resolve().name}: {len(findings)} finding(s)", ""]
    if not findings:
        return "\n".join(lines + ["No known traps found.", ""])
    lines += ["| Severity | Where | What | Fix | Proven in |", "|---|---|---|---|---|"]
    for f in findings:
        lines.append(f"| {f.severity} | `{f.path}:{f.line}` | {f.title} | {f.fix} | `{f.skill}` |")
    return "\n".join(lines + [""])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("roots", nargs="+")
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--fail-on", choices=SEVERITIES[:2])
    args = parser.parse_args(argv)
    results = {root: scan(root) for root in args.roots}
    if args.json:
        print(json.dumps({root: [asdict(f) for f in fs] for root, fs in results.items()}, indent=2))
    else:
        print("\n".join(report(root, fs) for root, fs in results.items()))
    if args.fail_on:
        worst = SEVERITIES.index(args.fail_on)
        return int(any(SEVERITIES.index(f.severity) <= worst for fs in results.values() for f in fs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
