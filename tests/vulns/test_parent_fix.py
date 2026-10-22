import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "zero-vulns" / "scripts"))
import parent_fix  # noqa: E402

PKCS = [{"url": "GHSA-a", "versions": "[9.0.0, 9.0.20]"}, {"url": "GHSA-b", "versions": "[10.0.0, 10.0.12]"}]


def version(v, groups):
    return {"version": v, "dependencyGroups": [
        {"targetFramework": tfm, "dependencies": [{"id": "System.Security.Cryptography.Pkcs", "range": rng}]}
        for tfm, rng in groups.items()]}


class ParentFixTests(unittest.TestCase):
    def test_the_group_for_net10_is_the_highest_net_one_below_it(self):
        groups = [{"targetFramework": ".NETStandard2.0"}, {"targetFramework": "net8.0"}, {"targetFramework": "net9.0"},
                  {"targetFramework": ".NETFramework4.6.2"}]
        self.assertEqual("net9.0", parent_fix.pick_group(groups, "net10.0")["targetFramework"])
        self.assertEqual(".NETStandard2.0", parent_fix.pick_group(groups[:1], "net10.0")["targetFramework"])

    def test_story_the_newest_sqlclient_still_brings_a_vulnerable_pkcs_so_the_pin_is_the_technical_need(self):
        # Given every SqlClient version, the newest asking for Pkcs [9.0.18, ) on net9.0 (so on net10.0 too)
        entries = [version("6.1.6", {"net9.0": "[9.0.0, )"}), version("7.1.1", {"net9.0": "[9.0.18, )", "net8.0": "[8.0.1, )"})]
        # When we ask whether a parent upgrade fixes Pkcs
        newest, safe, _ = parent_fix.report(entries, "System.Security.Cryptography.Pkcs", PKCS, "net10.0")
        # Then the newest brings 9.0.18, which the advisory covers, and no parent version fixes it
        self.assertEqual(("7.1.1", "9.0.18"), (newest["parent"], newest["brings"]))
        self.assertEqual(["GHSA-a"], newest["advisories"])
        self.assertIsNone(safe)

    def test_a_parent_release_that_moves_the_floor_is_the_upgrade(self):
        entries = [version("7.1.1", {"net9.0": "[9.0.18, )"}), version("7.2.0", {"net9.0": "[9.0.21, )"}),
                   version("7.3.0", {"net9.0": "[9.0.22, )"})]
        _, safe, _ = parent_fix.report(entries, "System.Security.Cryptography.Pkcs", PKCS, "net10.0")
        self.assertEqual(("7.2.0", "9.0.21"), (safe["parent"], safe["brings"]))

    def test_never_a_downgrade_below_the_version_you_have(self):
        entries = [version("6.0.0", {"net9.0": "[9.0.21, )"}), version("7.1.1", {"net9.0": "[9.0.18, )"}),
                   version("7.2.0", {"net9.0": "[9.0.21, )"})]
        _, safe, _ = parent_fix.report(entries, "System.Security.Cryptography.Pkcs", PKCS, "net10.0", current="7.1.1")
        self.assertEqual("7.2.0", safe["parent"])

    def test_prereleases_are_left_out_unless_asked_for(self):
        entries = [version("7.1.1", {"net9.0": "[9.0.18, )"}), version("7.2.0-preview1", {"net9.0": "[9.0.21, )"})]
        self.assertIsNone(parent_fix.report(entries, "System.Security.Cryptography.Pkcs", PKCS, "net10.0")[1])
        self.assertEqual("7.2.0-preview1",
                         parent_fix.report(entries, "System.Security.Cryptography.Pkcs", PKCS, "net10.0", prerelease=True)[1]["parent"])


if __name__ == "__main__":
    unittest.main()
