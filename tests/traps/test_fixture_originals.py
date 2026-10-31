"""The known-traps scan still finds, in each fix-eval fixture's original, every check it found there when
the scan was last measured against it. A change to a check that stops it firing on a planted trap fails
here, without running a model. A new check that fires is fine; a lost one isn't.
"""
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "known-traps" / "scripts"))
import scan_traps  # noqa: E402

EVALS = ROOT / "tests" / "evals"

# What the scan finds in each original today (known-traps 1.5.0, 2026-10-31).
EXPECTED = {
    "fix-hard/ClinicReminders": {
        "app-changes-schema",
        "ar-sa-default-calendar",
        "base-address-loses-path",
        "quartz-cron-in-utc",
        "quartz-missed-run-dropped",
        "scopes-not-validated",
        "serilog-factory-with-redaction",
        "singleton-holds-dbcontext",
        "sqlclient-invariant-mode",
        "sqlclient-pkcs-unpinned",
        "timezone-without-fallback",
    },
    "fix-lab/HomeReadings": {
        "ar-sa-default-calendar",
        "scopes-not-validated",
    },
    "fix-sync/MeterSync": {
        "ar-sa-default-calendar",
        "base-address-loses-path",
        "cors-any-origin",
        "jwt-audience-not-validated",
        "scopes-not-validated",
        "singleton-holds-dbcontext",
        "timezone-without-fallback",
        "worker-cancellation-by-type",
    },
    "fix-labs/LabResults": {
        "channel-queue-dropped-on-stop",
        "keycloak-roles-unmapped",
        "mask-misses-arabic-digits",
        "pooled-context-resolves-service",
        "scopes-not-validated",
    },
}


class FixtureOriginalsTests(unittest.TestCase):
    def test_every_expected_check_is_a_check_the_scan_has(self):
        known = {c[0] for c in scan_traps.CHECKS}
        unknown = {name: sorted(checks - known) for name, checks in EXPECTED.items() if checks - known}
        self.assertEqual({}, unknown)

    def test_the_scan_still_finds_what_it_found_in_each_original(self):
        for name, expected in EXPECTED.items():
            with self.subTest(fixture=name):
                folder = EVALS / name
                self.assertTrue(folder.is_dir(), f"{folder} is missing")
                found = {f.check for f in scan_traps.scan(folder)}
                self.assertEqual(set(), expected - found, f"{name}: checks the scan no longer finds")


if __name__ == "__main__":
    unittest.main()
