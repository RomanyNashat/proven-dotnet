"""upgrade_preflight.py: before a service moves from .NET 8 or 9 to 10, it names each line a known break in
.NET 9/10, ASP.NET Core 9/10, EF Core 9/10 or Npgsql 9/10 touches. Each check fires on the service that has
it and stays quiet on the one that doesn't."""
import contextlib
import io
import json
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "dotnet-upgrade" / "scripts"))
import upgrade_preflight  # noqa: E402

CHECK_IDS = [c.id for c in upgrade_preflight.CHECKS]
PROJECT = '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>{}</ItemGroup></Project>'


def ref(name, version="8.0.10"):
    return f'<PackageReference Include="{name}" Version="{version}" />'


def scan(files):
    with tempfile.TemporaryDirectory() as tmp:
        for rel, text in files.items():
            path = pathlib.Path(tmp) / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        return upgrade_preflight.scan(tmp)


def ids(files):
    return {f.check for f in scan(files).findings}


# Each check: a service that has the break, and the same service without it.
CASES = {
    "binary-formatter": (
        {"Api.csproj": PROJECT.format(""), "Cache.cs": "var f = new BinaryFormatter();"},
        {"Api.csproj": PROJECT.format(""), "Cache.cs": "var json = JsonSerializer.Serialize(item);"}),
    "versionless-package": (
        {"Api.csproj": PROJECT.format('<PackageReference Include="Dapper" />')},
        {"Api.csproj": PROJECT.format('<PackageReference Include="Dapper" />'),
         "Directory.Packages.props": '<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>'}),
    "system-linq-async": (
        {"Api.csproj": PROJECT.format(ref("System.Linq.Async", "6.0.1"))},
        {"Api.csproj": PROJECT.format("")}),
    "ef-hasdata-dynamic": (
        {"Api.csproj": PROJECT.format(""), "Config.cs": "b.HasData(new Clinic { Id = 1, CreatedAt = DateTime.UtcNow });"},
        {"Api.csproj": PROJECT.format(""), "Config.cs": "b.HasData(new Clinic { Id = 1, CreatedAt = new DateTime(2026, 1, 1) });"}),
    "forwarded-known-networks": (
        {"Api.csproj": PROJECT.format(""), "Program.cs": "o.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(\"10.0.0.0\"), 8));"},
        {"Api.csproj": PROJECT.format(""), "Program.cs": "o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(\"10.0.0.0/8\"));"}),
    "with-openapi": (
        {"Api.csproj": PROJECT.format(""), "Program.cs": "app.MapGet(\"/v\", Get).WithOpenApi();"},
        {"Api.csproj": PROJECT.format(""), "Program.cs": "app.MapGet(\"/v\", Get).WithSummary(\"Get\");"}),
    "action-context-accessor": (
        {"Api.csproj": PROJECT.format(""), "Links.cs": "public Links(IActionContextAccessor accessor) { }"},
        {"Api.csproj": PROJECT.format(""), "Links.cs": "public Links(IHttpContextAccessor accessor) { }"}),
    "webhost-builder": (
        {"Api.csproj": PROJECT.format(""), "Program.cs": "var host = new WebHostBuilder().UseKestrel().Build();"},
        {"Api.csproj": PROJECT.format(""), "Program.cs": "var builder = WebApplication.CreateBuilder(args);"}),
    "forwarded-headers-trust": (
        {"Api.csproj": PROJECT.format(""), "Program.cs": "app.UseForwardedHeaders();"},
        {"Api.csproj": PROJECT.format(""), "Program.cs": "o.KnownProxies.Add(IPAddress.Parse(\"10.0.0.5\"));\napp.UseForwardedHeaders();"}),
    "exception-handler-diagnostics": (
        {"Api.csproj": PROJECT.format(""), "Errors.cs": "sealed class Errors : IExceptionHandler {\n  public async ValueTask<bool> TryHandleAsync(HttpContext c, Exception e, CancellationToken ct) {\n    await c.Response.WriteAsJsonAsync(new { });\n    return true;\n  }\n}"},
        {"Api.csproj": PROJECT.format(""), "Errors.cs": "sealed class Errors : IExceptionHandler {\n  public ValueTask<bool> TryHandleAsync(HttpContext c, Exception e, CancellationToken ct) => ValueTask.FromResult(false);\n}"}),
    "background-service-start": (
        {"Api.csproj": PROJECT.format(""), "Worker.cs": "sealed class Releaser : BackgroundService { protected override async Task ExecuteAsync(CancellationToken t) { } }"},
        {"Api.csproj": PROJECT.format(""), "Worker.cs": "sealed class Releaser : IHostedService { }"}),
    "npgsql-date-read": (
        {"Api.csproj": PROJECT.format(ref("Npgsql", "8.0.5")), "Reads.cs": "var day = await c.ExecuteScalarAsync<DateTime>(\"select visit_date from visit\");"},
        {"Api.csproj": PROJECT.format(ref("Npgsql", "8.0.5")), "Reads.cs": "var day = await c.ExecuteScalarAsync<DateOnly>(\"select visit_date from visit\");"}),
    "npgsql-connection-lifetime": (
        {"Api.csproj": PROJECT.format(ref("Npgsql", "8.0.5")), "appsettings.json": '{"ConnectionStrings":{"Db":"Host=db;Database=visits"}}'},
        {"Api.csproj": PROJECT.format(ref("Npgsql", "8.0.5")), "appsettings.json": '{"ConnectionStrings":{"Db":"Host=db;Database=visits;Connection Lifetime=0"}}'}),
    "sqlserver-application-name": (
        {"Api.csproj": PROJECT.format(ref("Microsoft.EntityFrameworkCore.SqlServer")), "appsettings.json": '{"ConnectionStrings":{"Db":"Server=db;Database=Visits"}}'},
        {"Api.csproj": PROJECT.format(ref("Microsoft.EntityFrameworkCore.SqlServer")), "appsettings.json": '{"ConnectionStrings":{"Db":"Server=db;Database=Visits;Application Name=visits-api"}}'}),
    "azure-sql-json": (
        {"Api.csproj": PROJECT.format(""), "Program.cs": "o.UseAzureSql(connection);"},
        {"Api.csproj": PROJECT.format(""), "Program.cs": "o.UseSqlServer(connection);"}),
    "framework-package-reference": (
        {"Api.csproj": PROJECT.format(ref("System.Text.Json", "8.0.5"))},
        {"Api.csproj": PROJECT.format("")}),
    "image-tag": (
        {"Api.csproj": PROJECT.format(""), "Dockerfile": "FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine\n"},
        {"Api.csproj": PROJECT.format(""), "Dockerfile": "FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine\n"}),
}


class UpgradePreflightTests(unittest.TestCase):
    def test_every_check_has_a_case(self):
        self.assertEqual(sorted(CHECK_IDS), sorted(CASES))

    def test_each_check_fires_on_the_break_and_not_on_the_service_without_it(self):
        for check, (bad, good) in CASES.items():
            with self.subTest(check):
                self.assertIn(check, ids(bad))
                self.assertNotIn(check, ids(good))

    def test_every_check_is_in_the_skill(self):
        text = (ROOT / ".claude" / "skills" / "dotnet-upgrade" / "SKILL.md").read_text(encoding="utf-8")
        self.assertEqual([], [c for c in CHECK_IDS if f"`{c}`" not in text])

    def test_story_a_dotnet_8_service_gets_its_own_checklist_before_the_upgrade(self):
        # Given a .NET 8 API behind nginx with a global exception handler and Dapper on PostgreSQL
        result = scan({
            "src/Api/Api.csproj": PROJECT.format(ref("Npgsql", "8.0.5") + ref("Dapper", "2.1.35")),
            "src/Api/Program.cs": "app.UseForwardedHeaders();\napp.UseExceptionHandler();",
            "src/Api/Errors.cs": "sealed class Errors : IExceptionHandler {\n  public async ValueTask<bool> TryHandleAsync(HttpContext c, Exception e, CancellationToken ct) { return true; }\n}",
            "src/Api/Reads.cs": "var days = await c.QueryAsync<DateTime>(\"select visit_date from visit\");",
            "src/Api/appsettings.json": '{"ConnectionStrings":{"Db":"Host=db;Database=visits"}}',
        })
        # When it is scanned, then it names the framework and each break that touches it, worst first
        self.assertEqual(["net8.0"], result.frameworks)
        self.assertEqual(["forwarded-headers-trust", "exception-handler-diagnostics", "npgsql-connection-lifetime", "npgsql-date-read"],
                         [f.check for f in result.findings])
        self.assertEqual("src/Api/Errors.cs", next(f for f in result.findings if f.check == "exception-handler-diagnostics").path)

    def test_story_a_service_already_on_10_with_nothing_left_says_so(self):
        out = io.StringIO()
        with tempfile.TemporaryDirectory() as tmp:
            (pathlib.Path(tmp) / "Api.csproj").write_text(PROJECT.replace("net8.0", "net10.0").format(""), encoding="utf-8")
            with contextlib.redirect_stdout(out):
                code = upgrade_preflight.main([tmp])
        self.assertEqual(0, code)
        self.assertIn("net10.0", out.getvalue())
        self.assertIn("Nothing found", out.getvalue())

    def test_commented_out_lines_and_build_output_are_not_findings(self):
        found = ids({"Api.csproj": PROJECT.format(""), "Program.cs": "// app.MapGet(\"/v\", Get).WithOpenApi();",
                     "bin/Debug/Old.cs": "var f = new BinaryFormatter();"})
        self.assertEqual(set(), found)

    def test_json_output_lists_the_frameworks_and_findings(self):
        out = io.StringIO()
        with tempfile.TemporaryDirectory() as tmp:
            (pathlib.Path(tmp) / "Api.csproj").write_text(PROJECT.format(ref("System.Linq.Async", "6.0.1")), encoding="utf-8")
            with contextlib.redirect_stdout(out):
                upgrade_preflight.main([tmp, "--json"])
        data = json.loads(out.getvalue())[tmp]
        self.assertEqual(["net8.0"], data["frameworks"])
        self.assertEqual(["system-linq-async"], [f["check"] for f in data["findings"]])


if __name__ == "__main__":
    unittest.main()
