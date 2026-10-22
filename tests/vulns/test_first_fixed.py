import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "zero-vulns" / "scripts"))
import first_fixed as ff  # noqa: E402

# The shape of nuget.org's vulnerability data for one package (the Pkcs advisories, October 2026).
ADVISORIES = [
    {"url": "https://github.com/advisories/GHSA-846f-f764-76mx", "severity": 2, "versions": "[10.0.0, 10.0.12]"},
    {"url": "https://github.com/advisories/GHSA-846f-f764-76mx", "severity": 2, "versions": "[9.0.0, 9.0.20]"},
    {"url": "https://github.com/advisories/GHSA-846f-f764-76mx", "severity": 2, "versions": "[11.0.0-preview.1, 11.0.0-rc.1]"},
    {"url": "https://github.com/advisories/GHSA-2fmv-rv2c-59x7", "severity": 1, "versions": "[10.0.0, 10.0.12]"},
]
VERSIONS = ["8.0.1", "9.0.0", "9.0.11", "9.0.20", "9.0.21", "10.0.0", "10.0.12", "10.0.13",
            "11.0.0-preview.1", "11.0.0-rc.1", "11.0.0-rc.2"]


class VersionTests(unittest.TestCase):
    def test_versions_sort_numerically_and_a_release_after_its_prereleases(self):
        ordered = sorted(["10.0.0", "9.0.21", "10.0.0-rc.2", "10.0.0-rc.10", "9.0.3"], key=ff.parse_version)
        self.assertEqual(["9.0.3", "9.0.21", "10.0.0-rc.2", "10.0.0-rc.10", "10.0.0"], ordered)

    def test_ranges_in_interval_notation(self):
        self.assertTrue(ff.in_range(ff.parse_version("9.0.20"), ff.parse_range("[9.0.0, 9.0.20]")))
        self.assertFalse(ff.in_range(ff.parse_version("9.0.20"), ff.parse_range("[9.0.0, 9.0.20)")))
        self.assertTrue(ff.in_range(ff.parse_version("1.0.0"), ff.parse_range("(, 2.0.0)")))
        self.assertFalse(ff.in_range(ff.parse_version("2.0.0"), ff.parse_range("(2.0.0, )")))
        self.assertTrue(ff.in_range(ff.parse_version("3.0.0"), ff.parse_range("2.0.0")))   # bare: that or later
        self.assertTrue(ff.in_range(ff.parse_version("4.1.0"), ff.parse_range("[4.1.0]")))


class FirstFixedTests(unittest.TestCase):
    def test_story_the_obvious_upgrade_is_flagged_too(self):
        # Given: a service pulls Pkcs 9.0.11 through SqlClient, and the audit fails the build
        # When: someone reaches for the new major, 10.0.0
        # Then: 10.0.0 has the same advisories; the first safe versions are 9.0.21 on the same line and 10.0.13
        self.assertEqual(2, len(ff.advisories_for("10.0.0", ADVISORIES)))
        self.assertEqual(("9.0.21", "9.0.21"), ff.first_fixed("9.0.11", VERSIONS, ADVISORIES))
        self.assertEqual(("10.0.13", "10.0.13"), ff.first_fixed("10.0.0", VERSIONS, ADVISORIES))

    def test_prereleases_count_only_when_asked(self):
        self.assertEqual((None, None), ff.first_fixed("10.0.13", VERSIONS, ADVISORIES))
        self.assertEqual((None, "11.0.0-rc.2"), ff.first_fixed("10.0.13", VERSIONS, ADVISORIES, prerelease=True))

    def test_no_safe_version_yet_is_no_answer(self):
        advisories = [{"url": "x", "severity": 3, "versions": "[1.0.0, )"}]
        self.assertEqual((None, None), ff.first_fixed("1.0.0", ["1.0.0", "1.1.0", "2.0.0"], advisories))


if __name__ == "__main__":
    unittest.main()
