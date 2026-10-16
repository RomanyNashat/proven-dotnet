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
