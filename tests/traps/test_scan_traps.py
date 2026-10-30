import contextlib
import io
import json
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SKILL = ROOT / ".claude" / "skills" / "known-traps"
sys.path.insert(0, str(SKILL / "scripts"))
import scan_traps  # noqa: E402

FIXTURE = pathlib.Path(__file__).parent / "fixture"
BAD, GOOD = FIXTURE / "bad", FIXTURE / "good"
CHECK_IDS = [c[0] for c in scan_traps.CHECKS]


def by_check(findings):
    found = {}
    for f in findings:
        found.setdefault(f.check, []).append(f)
    return found


def write(folder, files):
    for rel, text in files.items():
        path = folder / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


class ScanTrapsTests(unittest.TestCase):
    def test_every_check_fires_on_the_service_that_has_the_trap(self):
        missing = set(CHECK_IDS) - {f.check for f in scan_traps.scan(BAD)}
        self.assertEqual(set(), missing)

    def test_the_corrected_service_has_no_findings(self):
        self.assertEqual([], [(f.check, f.path, f.line) for f in scan_traps.scan(GOOD)])

    def test_every_check_is_documented_in_the_skill(self):
        text = (SKILL / "SKILL.md").read_text(encoding="utf-8")
        self.assertEqual([], [c for c in CHECK_IDS if f"`{c}`" not in text])

    def test_finding_points_at_the_file_and_line(self):
        cors = by_check(scan_traps.scan(BAD))["cors-any-origin"]
        self.assertEqual([("src/Clinic.Api/Program.cs", 7)], [(f.path, f.line) for f in cors])

    def test_a_group_built_from_a_parameter_is_caught_too(self):
        lines = [f.line for f in by_check(scan_traps.scan(BAD))["signalr-client-picks-group"]]
        self.assertEqual([5, 7], lines)   # the bare parameter, and $"patient-{patientId}"

    def test_schema_change_is_critical_in_the_app_and_a_warning_in_a_test_fixture(self):
        found = {(f.path, f.severity) for f in by_check(scan_traps.scan(BAD))["app-changes-schema"]}
        self.assertEqual({("src/Clinic.Api/Program.cs", "critical"),
                          ("tests/Clinic.Tests/DatabaseFixture.cs", "warning")}, found)

    def test_story_reviewing_a_change_lists_what_it_adds_and_counts_the_rest(self):
        # Given a merge request that adds CORS to Program.cs (line 7) in a service with many old traps,
        # its diff written from the repo root, where the service is one folder among others
        diff = """diff --git a/services/clinic/src/Clinic.Api/Program.cs b/services/clinic/src/Clinic.Api/Program.cs
--- a/services/clinic/src/Clinic.Api/Program.cs
+++ b/services/clinic/src/Clinic.Api/Program.cs
@@ -6,0 +7 @@ var builder = WebApplication.CreateBuilder(args);
+builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.SetIsOriginAllowed(_ => true)));
"""
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "change.diff"
            path.write_text(diff, encoding="utf-16")   # as Windows PowerShell's `>` writes it
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = scan_traps.main([str(BAD), "--diff", str(path), "--json", "--fail-on", "critical"])
            findings = list(json.loads(out.getvalue()).values())[0]
        # Then the CORS finding is the change's, the rest of Program.cs is touched, everything else outside
        self.assertEqual(["cors-any-origin"], [f["check"] for f in findings if f["change"] == "changed"])
        touched = {f["path"] for f in findings if f["change"] == "touched"}
        self.assertEqual({"src/Clinic.Api/Program.cs"}, touched)
        self.assertTrue(any(f["change"] == "outside" for f in findings))
        # and --fail-on judges the change: the critical traps elsewhere in the file are touched, not outside
        self.assertEqual(1, code)

    def test_a_change_outside_every_finding_fails_nothing(self):
        diff = "+++ b/README.md\n@@ -1,0 +1 @@\n+a line\n"
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "change.diff"
            path.write_text(diff, encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                code = scan_traps.main([str(BAD), "--diff", str(path), "--fail-on", "warning"])
        self.assertEqual(0, code)
        self.assertIn("0 finding(s) in the change", out.getvalue())

    def test_fail_on_sets_the_exit_code(self):
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, scan_traps.main([str(BAD), "--fail-on", "critical"]))
            self.assertEqual(0, scan_traps.main([str(GOOD), "--fail-on", "warning"]))

    def test_json_output_lists_findings_per_service(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            scan_traps.main([str(GOOD), str(BAD), "--json"])
        data = json.loads(out.getvalue())
        self.assertEqual([], data[str(GOOD)])
        self.assertEqual(set(CHECK_IDS), {f["check"] for f in data[str(BAD)]})

    def test_story_pin_moved_to_central_package_management_is_still_seen(self):
        # Given a service that moved onto Central Package Management: the SqlClient reference has no version,
        # and the Pkcs pin is a PackageVersion in Directory.Packages.props
        with tempfile.TemporaryDirectory() as tmp:
            folder = pathlib.Path(tmp)
            write(folder, {
                "Directory.Packages.props": '<Project><ItemGroup>'
                    '<PackageVersion Include="Microsoft.Data.SqlClient" Version="6.1.6" />'
                    '<PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />'
                    '</ItemGroup></Project>',
                "src/Api/Api.csproj": '<Project><ItemGroup><PackageReference Include="Microsoft.Data.SqlClient" />'
                    '</ItemGroup></Project>',
            })
            # When the fleet is scanned, then the central pin counts and the service is clean
            self.assertEqual([], scan_traps.scan(folder))

    def test_story_file_saved_by_a_windows_editor_with_a_bom_is_still_read(self):
        # Given a Program.cs saved with a UTF-8 byte-order mark, as Visual Studio writes it
        with tempfile.TemporaryDirectory() as tmp:
            path = pathlib.Path(tmp) / "Program.cs"
            path.write_bytes("﻿builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.SetIsOriginAllowed(_ => true)));"
                             .encode("utf-8"))
            # Then the scan still reads it and finds the trap on line 1
            self.assertEqual([("cors-any-origin", 1)], [(f.check, f.line) for f in scan_traps.scan(tmp)])

    def test_a_startup_check_that_ar_sa_exists_is_not_a_finding(self):
        # Given a guard that only checks the culture is there (it fails start-up without ICU), next to a
        # culture that sets its calendar in another file
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"Api/GlobalizationGuard.cs":
                'try { _ = CultureInfo.GetCultureInfo("ar-SA", predefinedOnly: true); } catch (CultureNotFoundException) { throw; }\n'})
            self.assertEqual([], scan_traps.scan(tmp))

    def test_story_ar_sa_made_with_a_target_typed_new_is_still_seen(self):
        # Given a service that writes `CultureInfo Arabic = new("ar-SA")`, the form C# 9 and later allow
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"AlertText.cs": 'private static readonly CultureInfo Arabic = new("ar-SA");\n'})
            # Then the scan reports it as it does `new CultureInfo("ar-SA")`
            self.assertEqual([("ar-sa-default-calendar", 1)], [(f.check, f.line) for f in scan_traps.scan(tmp)])

    def test_a_worker_that_lets_a_timeout_through_is_seen_and_a_catch_elsewhere_is_not(self):
        catch = "catch (Exception ex) when (ex is not OperationCanceledException) { }\n"
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"Worker.cs": "class W : BackgroundService {\n" + catch + "}\n",
                                      "Endpoints.cs": "static class E {\n" + catch + "}\n"})
            self.assertEqual([("worker-cancellation-by-type", "Worker.cs", 2)],
                             [(f.check, f.path, f.line) for f in scan_traps.scan(tmp)])

    def test_a_pooled_context_reading_configuration_is_not_a_finding_a_scoped_interceptor_is(self):
        # Given pooled options that read the connection string from IConfiguration and add a scoped interceptor
        program = ('services.AddScoped<AuditInterceptor>();\n'
                   'services.AddPooledDbContextFactory<LabDb>((sp, o) => o\n'
                   '    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Lab"))\n'
                   '    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));\n')
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"Program.cs": program})
            # Then only the interceptor's line is a place to read: configuration is the host's own singleton
            self.assertEqual([("pooled-context-resolves-service", 4)], [(f.check, f.line) for f in scan_traps.scan(tmp)])

    def test_a_mask_that_turns_digits_western_first_and_a_call_to_a_mask_are_not_findings(self):
        normalised = ('static partial class Masking {\n'
                      '    static string Western(string v) => new(v.Select(c => char.IsDigit(c) ? (char)(\'0\' + (int)char.GetNumericValue(c)) : c).ToArray());\n'
                      '    [GeneratedRegex("[0-9]{10}")] private static partial Regex TenDigits();\n}\n')
        caller = 'static class Endpoints {\n    static bool Valid(string id) => Regex.IsMatch(id, "^[0-9]{10}$") && Masking.NationalId(id) != "";\n}\n'
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"Masking.cs": normalised, "Endpoints.cs": caller})
            self.assertEqual([], [f.check for f in scan_traps.scan(tmp) if f.check == "mask-misses-arabic-digits"])

    def test_commented_out_code_is_not_a_finding(self):
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"Program.cs": "// p.SetIsOriginAllowed(_ => true)\n/* old:\n * ValidateAudience = false\n */\n"})
            self.assertEqual([], scan_traps.scan(tmp))

    def test_build_output_is_not_scanned(self):
        with tempfile.TemporaryDirectory() as tmp:
            write(pathlib.Path(tmp), {"src/Api/obj/Generated.cs": "x.SetIsOriginAllowed(_ => true);"})
            self.assertEqual([], scan_traps.scan(tmp))


if __name__ == "__main__":
    unittest.main()
