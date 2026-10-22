import json
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".claude" / "skills" / "msbuild" / "scripts"))
import cpm_check  # noqa: E402

API = '''<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
    <PackageReference Include="Serilog" Version="4.*" />
  </ItemGroup>
</Project>'''
TESTS = '''<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
    <PackageReference Include="xunit"><Version>2.9.2</Version></PackageReference>
  </ItemGroup>
</Project>'''


def write(root, files):
    for rel, text in files.items():
        path = pathlib.Path(root) / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


def converted(versions):
    """The solution after a move onto CPM: no versions in the projects, these in Directory.Packages.props."""
    items = "\n".join(f'    <PackageVersion Include="{p}" Version="{v}" />' for p, v in versions.items())
    return {
        "Directory.Packages.props": f"<Project>\n  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>\n  <ItemGroup>\n{items}\n  </ItemGroup>\n</Project>",
        "src/Api/Api.csproj": '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Newtonsoft.Json" /><PackageReference Include="Serilog" /></ItemGroup></Project>',
        "tests/Tests/Tests.csproj": '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Newtonsoft.Json" /><PackageReference Include="FluentAssertions" /><PackageReference Include="xunit" /></ItemGroup></Project>',
    }


class CpmCheckTests(unittest.TestCase):
    def setUp(self):
        self.before_dir = tempfile.TemporaryDirectory()
        write(self.before_dir.name, {"src/Api/Api.csproj": API, "tests/Tests/Tests.csproj": TESTS,
                                     "src/Api/obj/ignored.props": "<Project/>"})
        self.before = cpm_check.entries(self.before_dir.name)

    def tearDown(self):
        self.before_dir.cleanup()

    def test_snapshot_keeps_versions_as_written(self):
        written = {(e["package"], e["version"]) for e in self.before}
        self.assertIn(("FluentAssertions", "[7.0.0,8.0.0)"), written)
        self.assertIn(("Serilog", "4.*"), written)
        self.assertIn(("xunit", "2.9.2"), written)   # a <Version> child element counts too

    def test_story_a_conversion_that_only_knows_exact_versions_loses_the_licence_fence(self):
        # Given: a solution whose test project fences FluentAssertions below 8 (a paid licence from 8.0)
        # When: it's moved onto CPM by something that drops what it can't parse as one version
        with tempfile.TemporaryDirectory() as after:
            write(after, converted({"Newtonsoft.Json": "13.0.3", "xunit": "2.9.2"}))
            errors, changes = cpm_check.compare(self.before, after)
        # Then: the lost fence and the lost package are errors, and the 12 -> 13 move is reported
        self.assertTrue(any("FluentAssertions" in e for e in errors), errors)
        self.assertTrue(any("Serilog" in e and "no central version" in e for e in errors), errors)
        self.assertEqual(["src/Api/Api.csproj: Newtonsoft.Json 12.0.3 -> 13.0.3"], changes)

    def test_a_range_turned_into_one_version_is_an_error(self):
        with tempfile.TemporaryDirectory() as after:
            write(after, converted({"Newtonsoft.Json": "13.0.3", "xunit": "2.9.2", "Serilog": "4.*",
                                    "FluentAssertions": "8.0.0"}))
            errors, _ = cpm_check.compare(self.before, after)
        self.assertEqual(["tests/Tests/Tests.csproj: FluentAssertions [7.0.0,8.0.0): the range became 8.0.0"], errors)

    def test_a_careful_move_has_only_the_change_it_chose(self):
        with tempfile.TemporaryDirectory() as after:
            write(after, converted({"Newtonsoft.Json": "13.0.3", "xunit": "2.9.2", "Serilog": "4.3.0",
                                    "FluentAssertions": "[7.0.0,8.0.0)"}))
            errors, changes = cpm_check.compare(self.before, after)
            self.assertEqual(0, cpm_check.main(["compare", self._save(), after]))
        self.assertEqual([], errors)
        # the two changes it chose: one version for Newtonsoft.Json, and Serilog's float made exact
        self.assertEqual(["src/Api/Api.csproj: Newtonsoft.Json 12.0.3 -> 13.0.3",
                          "src/Api/Api.csproj: Serilog 4.* -> 4.3.0"], changes)

    def test_an_exact_pin_in_brackets_is_not_a_fence(self):
        self.assertFalse(cpm_check.is_fence("[7.0.0]"))
        self.assertTrue(cpm_check.is_fence("[7.0.0,8.0.0)"))
        self.assertFalse(cpm_check.is_fence("10.*"))
        self.assertTrue(cpm_check.is_fence("(,8.0.0)"))
        self.assertFalse(cpm_check.is_fence("10.0.13"))

    def _save(self):
        path = pathlib.Path(self.before_dir.name) / "before.json"
        path.write_text(json.dumps(self.before), encoding="utf-8")
        return str(path)


if __name__ == "__main__":
    unittest.main()
