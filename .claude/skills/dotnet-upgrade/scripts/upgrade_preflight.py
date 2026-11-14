#!/usr/bin/env python3
"""Before a service moves from .NET 8 or 9 to .NET 10: the lines a known break touches.

    python upgrade_preflight.py <service folder> [<more folders>] [--json]

Read-only, standard library only, no build or restore. For each service it prints the target frameworks
it found and a table of findings, worst first:

    breaks     won't build, or fails at run time, once on 10
    obsolete   a warning on 10 (an error under warnings-as-errors)
    behaviour  builds and runs, but does something different
    check      may be affected: read the line

Each finding names the file and line, what changed and in which release, and what to do. It's a text
scan: a finding is a place to read and a clean result isn't a proof. The sections of `dotnet-upgrade`
explain the breaks, and the ones it tests say so.
"""
import argparse
import json
import pathlib
import re
import sys
from dataclasses import asdict, dataclass, field

SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".vs", ".idea", "artifacts", "TestResults"}
SEVERITIES = ("breaks", "obsolete", "behaviour", "check")
COMMENT = re.compile(r"^\s*(//|\*|/\*|#)")
FRAMEWORK_PACKAGES = ("System.Text.Json", "System.Memory", "System.Buffers", "System.Threading.Channels")


@dataclass
class Check:
    id: str
    severity: str
    since: str
    title: str
    todo: str
    find: object


@dataclass
class Finding:
    check: str
    severity: str
    since: str
    path: str
    line: int
    title: str
    todo: str


@dataclass
class Result:
    frameworks: list = field(default_factory=list)
    findings: list = field(default_factory=list)


CHECKS = []


def check(check_id, severity, since, title, todo):
    def register(func):
        CHECKS.append(Check(check_id, severity, since, title, todo, func))
        return func
    return register


class Service:
    def __init__(self, root):
        self.files = {}
        for path in sorted(pathlib.Path(root).rglob("*")):
            rel = path.relative_to(root).as_posix()
            if path.is_file() and not SKIP_DIRS.intersection(rel.split("/")[:-1]) and self.wanted(path.name):
                self.files[rel] = path.read_text(encoding="utf-8-sig", errors="replace")

    @staticmethod
    def wanted(name):
        lower = name.lower()
        return (lower.endswith((".cs", ".csproj", ".props", ".targets"))
                or lower.startswith("dockerfile") or (lower.startswith("appsettings") and lower.endswith(".json")))

    def of(self, *suffixes):
        return [(rel, text) for rel, text in self.files.items() if rel.lower().endswith(suffixes)]

    def has(self, pattern, *suffixes):
        return any(any(True for _ in lines(text, pattern)) for _, text in self.of(*suffixes))

    def packages(self):
        """{package: [(rel, version, line)]} from PackageReference and PackageVersion."""
        found = {}
        for rel, text in self.of(".csproj", ".props", ".targets"):
            for tag in re.finditer(r"<Package(?:Reference|Version)\b[^>]*>", text):
                attrs = dict(re.findall(r'(\w+)="([^"]*)"', tag[0]))
                if "Include" in attrs:
                    found.setdefault(attrs["Include"], []).append(
                        (rel, attrs.get("Version", attrs.get("VersionOverride", "")), line_of(text, tag.start())))
        return found

    def frameworks(self):
        names = []
        for _, text in self.of(".csproj", ".props", ".targets"):
            for _, match in lines(text, r"<TargetFrameworks?>([^<]+)</TargetFrameworks?>"):
                names += [n.strip() for n in match[1].split(";") if n.strip() and n.strip() not in names]
        return names


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def lines(text, pattern, flags=0):
    """Matches outside comment lines, as (line number, match)."""
    for match in re.finditer(pattern, text, flags):
        start = text.rfind("\n", 0, match.start()) + 1
        end = text.find("\n", match.start())
        if not COMMENT.match(text[start:end if end != -1 else len(text)]):
            yield line_of(text, match.start()), match


def call_text(text, index):
    """From index to the ')' that closes the first '(' after it."""
    start, depth = text.find("(", index), 0
    for i in range(start, len(text)):
        depth += {"(": 1, ")": -1}.get(text[i], 0)
        if depth == 0:
            return text[index:i + 1]
    return text[index:]


def each(service, pattern, *suffixes, flags=0):
    for rel, text in service.of(*suffixes):
        for line, _ in lines(text, pattern, flags):
            yield rel, line


def connection_strings(service, marker):
    """(rel, line) of each appsettings connection string that contains marker (case-insensitive)."""
    for rel, text in service.of(".json"):
        for line, match in lines(text, r'"[^"]*"\s*:\s*"([^"]*)"'):
            if marker.lower() in match[1].lower():
                yield rel, line, match[1]


# --- breaks --------------------------------------------------------------------------------------------

@check("binary-formatter", "breaks", ".NET 9", "BinaryFormatter always throws",
       "Move to System.Text.Json (or another serializer) before the upgrade; data already stored needs a one-off conversion.")
def binary_formatter(service):
    yield from each(service, r"\bBinaryFormatter\b", ".cs")


@check("versionless-package", "breaks", "SDK 10", "A PackageReference without a version is an error",
       "Give it a Version, or move the repo onto Central Package Management (`msbuild`).")
def versionless_package(service):
    if any(re.search(r"<ManagePackageVersionsCentrally>\s*true", t, re.I) for _, t in service.of(".props", ".csproj", ".targets")):
        return
    for rel, text in service.of(".csproj"):
        for line, match in lines(text, r"<PackageReference\b[^>]*>"):
            if "Include=" in match[0] and not re.search(r"\b(Version|VersionOverride)=", match[0]):
                yield rel, line


@check("system-linq-async", "breaks", ".NET 10", "System.Linq.Async next to the built-in async LINQ",
       "Remove the package: calls such as ToListAsync() on an IAsyncEnumerable<T> become ambiguous.")
def system_linq_async(service):
    for rel, _, line in service.packages().get("System.Linq.Async", []):
        yield rel, line


@check("ef-hasdata-dynamic", "breaks", "EF Core 9", "HasData with a value that changes on every build",
       "Use a fixed value (new DateTime(2026, 1, 1), a fixed Guid): EF Core 9 sees a model change every time and the next migration script is never empty.")
def ef_hasdata_dynamic(service):
    for rel, text in service.of(".cs"):
        for line, match in lines(text, r"\bHasData\s*\("):
            if re.search(r"DateTime(Offset)?\.(Now|UtcNow|Today)|Guid\.NewGuid\(\)", call_text(text, match.start())):
                yield rel, line


# --- obsolete ------------------------------------------------------------------------------------------

@check("forwarded-known-networks", "obsolete", "ASP.NET Core 10", "ForwardedHeadersOptions.KnownNetworks and its IPNetwork",
       "Use KnownIPNetworks with System.Net.IPNetwork (IPNetwork.Parse(\"10.0.0.0/8\")).")
def forwarded_known_networks(service):
    yield from each(service, r"\bKnownNetworks\b|HttpOverrides\.IPNetwork\b", ".cs")


@check("with-openapi", "obsolete", "ASP.NET Core 10", "WithOpenApi() on an endpoint",
       "Describe the endpoint with WithSummary/WithDescription/Produces, or a document or operation transformer (`api-design`).")
def with_openapi(service):
    yield from each(service, r"\.WithOpenApi\s*\(", ".cs")


@check("action-context-accessor", "obsolete", "ASP.NET Core 10", "IActionContextAccessor / ActionContextAccessor",
       "Take what's needed from HttpContext (IHttpContextAccessor, or the endpoint's own parameters).")
def action_context_accessor(service):
    yield from each(service, r"\bI?ActionContextAccessor\b", ".cs")


@check("webhost-builder", "obsolete", "ASP.NET Core 10", "WebHostBuilder, IWebHost or WebHost",
       "Move the host to WebApplication.CreateBuilder (or Host.CreateApplicationBuilder for a worker).")
def webhost_builder(service):
    yield from each(service, r"\bnew\s+WebHostBuilder\b|\bWebHost\.Create\w*\(|\bIWebHost\b", ".cs")


@check("framework-package-reference", "obsolete", "SDK 10", "A package the framework already provides (NU1510)",
       "Remove the reference: the framework's copy is used and is the newer one.")
def framework_package_reference(service):
    packages = service.packages()
    for name in FRAMEWORK_PACKAGES:
        for rel, _, line in packages.get(name, []):
            yield rel, line


# --- behaviour -----------------------------------------------------------------------------------------

@check("forwarded-headers-trust", "behaviour", "ASP.NET Core 9", "Forwarded headers with no proxy named as trusted",
       "X-Forwarded-* from a proxy not in KnownProxies/KnownIPNetworks is ignored: behind nginx the client IP and scheme are lost (HTTPS redirect loops, wrong IP in logs). Add the ingress's proxies or networks (`nginx`).")
def forwarded_headers_trust(service):
    if service.has(r"\bKnown(Proxies|Networks|IPNetworks)\b", ".cs"):
        return
    yield from each(service, r"\bUseForwardedHeaders\s*\(", ".cs")


@check("exception-handler-diagnostics", "behaviour", "ASP.NET Core 10", "An IExceptionHandler that returns true",
       "On 10 a handled exception emits no diagnostics, so it can drop out of the logs and APM: log it in the handler.")
def exception_handler_diagnostics(service):
    for rel, text in service.of(".cs"):
        if re.search(r"\bIExceptionHandler\b", text):
            for line, _ in lines(text, r"\breturn\s+true\s*;|ValueTask\.FromResult\(\s*true\s*\)"):
                yield rel, line


@check("npgsql-connection-lifetime", "behaviour", "Npgsql 9", "Npgsql connections now close after an hour",
       "Connection Lifetime defaults to 3600 s. Keep it, or set it in the connection string on purpose.")
def npgsql_connection_lifetime(service):
    if not any(n == "Npgsql" or n.startswith("Npgsql.") for n in service.packages()):
        return
    for rel, line, text in connection_strings(service, "Host="):
        if "connection lifetime" not in text.lower() and "connectionlifetime" not in text.lower():
            yield rel, line


@check("sqlserver-application-name", "behaviour", "EF Core 10", "EF Core adds an Application Name to SQL Server connection strings",
       "Set Application Name yourself: EF and non-EF code on the same database otherwise use separate pools, and a transaction across them can become distributed.")
def sqlserver_application_name(service):
    if "Microsoft.EntityFrameworkCore.SqlServer" not in service.packages():
        return
    for marker in ("Server=", "Data Source="):
        for rel, line, text in connection_strings(service, marker):
            if "application name" not in text.lower():
                yield rel, line


@check("azure-sql-json", "behaviour", "EF Core 10", "UseAzureSql maps JSON to the json type",
       "New JSON columns become json, not nvarchar(max), and the upgrade generates an ALTER for existing ones: set UseCompatibilityLevel or the column type on purpose.")
def azure_sql_json(service):
    yield from each(service, r"\.UseAzureSql\s*\(", ".cs")


@check("background-service-start", "behaviour", ".NET 10", "BackgroundService runs all of ExecuteAsync as a Task",
       "Code before the first await no longer runs before the host finishes starting: move start-up work that others depend on to StartAsync.")
def background_service_start(service):
    yield from each(service, r":\s*BackgroundService\b", ".cs")


# --- check ---------------------------------------------------------------------------------------------

@check("npgsql-date-read", "check", "Npgsql 10", "A date or time read as DateTime or TimeSpan on Npgsql",
       "Npgsql 10 returns date as DateOnly and time as TimeOnly from untyped reads: read with GetFieldValue<DateTime>, or move to DateOnly/TimeOnly.")
def npgsql_date_read(service):
    if not any(n == "Npgsql" or n.startswith("Npgsql.") for n in service.packages()):
        return
    pattern = (r"<\s*(DateTime|TimeSpan)\s*>\s*\(|\(\s*(DateTime|TimeSpan)\s*\)\s*\w+(\.GetValue\(|\[|\.ExecuteScalar)")
    yield from each(service, pattern, ".cs")
    # The value is often read on one line and cast on a later one: follow the variable within the file.
    untyped = re.compile(r"\b(\w+)\s*=\s*(?:await\s+)?[\w.]+?(?:\.ExecuteScalar(?:Async)?\s*\(|\.GetValue\s*\(|\[)")
    for rel, text in service.of(".cs"):
        for name in sorted({m[1] for m in untyped.finditer(text)}):
            used = (rf"\(\s*(DateTime|TimeSpan)\??\s*\)\s*{name}\b|\b{name}\s+is\s+(DateTime|TimeSpan)\b"
                    rf"|Convert\.ToDateTime\(\s*{name}\b")
            for line, _ in lines(text, used):
                yield rel, line


@check("image-tag", "check", ".NET 10", "A .NET 8 or 9 base image",
       "The tag moves to 10.0, and the default images are Ubuntu since 10 (Debian before; 9 dropped zlib): package names in the image steps change.")
def image_tag(service):
    for rel, text in service.files.items():
        if pathlib.PurePosixPath(rel).name.lower().startswith("dockerfile"):
            for line, _ in lines(text, r"mcr\.microsoft\.com/dotnet/[\w-]+:(8|9)\.0", re.I):
                yield rel, line


def scan(root):
    service = Service(pathlib.Path(root))
    findings = []
    for order, c in enumerate(CHECKS):
        # Two patterns of one check can match the same line: report it once.
        for rel, line in sorted(set(c.find(service) or [])):
            findings.append((SEVERITIES.index(c.severity), order, rel, line,
                             Finding(c.id, c.severity, c.since, rel, line, c.title, c.todo)))
    return Result(service.frameworks(), [f for *_, f in sorted(findings, key=lambda x: x[:4])])


def report(root, result):
    name = pathlib.Path(root).resolve().name
    frameworks = ", ".join(result.frameworks) or "none found"
    out = [f"## {name}: {frameworks}, {len(result.findings)} finding(s)", ""]
    if not result.findings:
        return "\n".join(out + ["Nothing found that a known .NET 9 or 10 break touches.", ""])
    out += ["| Severity | Since | Where | What | What to do |", "|---|---|---|---|---|"]
    for f in result.findings:
        out.append(f"| {f.severity} | {f.since} | `{f.path}:{f.line}` | {f.title} | {f.todo} |")
    return "\n".join(out + [""])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("roots", nargs="+")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    results = {root: scan(root) for root in args.roots}
    if args.json:
        print(json.dumps({root: {"frameworks": r.frameworks, "findings": [asdict(f) for f in r.findings]}
                          for root, r in results.items()}, indent=2))
    else:
        print("\n".join(report(root, r) for root, r in results.items()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
