"""silent_failures.py on a coverage service (fixture/Coverage) with seven planted silent failures and the
right-looking code beside each that isn't one: what a review of a real service has to tell apart."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".claude" / "skills" / "code-quality" / "scripts" / "silent_failures.py"
FIXTURE = Path(__file__).resolve().parent / "fixture" / "Coverage"

PLANTED = {
    ("src/InsurerClient.cs", 12): "fallback",            # an insurer outage reads as 0% covered
    ("src/InsurerClient.cs", 24): "swallowed",           # a lost notification, no trace
    ("src/ClaimsJob.cs", 13): "logged and ignored",      # the claim is marked sent though it wasn't
    ("src/ClaimsJob.cs", 26): "skipped",                 # a claim that didn't archive is dropped
    ("src/ClaimsJob.cs", 32): "not awaited",             # the audit write's failure is never seen
    ("src/LabResults.cs", 8): "default for missing",     # no HbA1c on file reads as 0
    ("src/LabResults.cs", 13): "parse ignored",          # an age that didn't parse is 0
}


def scan(root, *args, env=None):
    out = subprocess.run([sys.executable, str(SCRIPT), str(root), "--json", *map(str, args)], capture_output=True, text=True,
                         encoding="utf-8", env=env)
    assert out.returncode == 0, out.stderr
    return {(f["file"], f["line"]): f for f in json.loads(out.stdout)}


class Stories(unittest.TestCase):
    def test_story_each_planted_failure_is_listed_with_its_kind(self):
        # Given a service where an insurer outage, a lost claim and a missing lab value all pass for success
        found = scan(FIXTURE)
        # Then each is listed where it is, with what kind of silence it is, and nothing else is
        self.assertEqual(PLANTED, {k: f["kind"] for k, f in found.items()})

    def test_the_right_looking_code_beside_them_isnt_listed(self):
        found = scan(FIXTURE)
        for line in (33, 37):   # a cancellation rethrown; an outage logged and wrapped for the caller
            self.assertNotIn(("src/InsurerClient.cs", line), found)
        for line in (11, 21):   # a 503 Problem; a timeout wrapped and rethrown
            self.assertNotIn(("src/CoverageEndpoints.cs", line), found)
        self.assertNotIn(("src/LabResults.cs", 17), found)   # TryParse whose result picks the answer
        self.assertNotIn(("src/LabResults.cs", 19), found)   # ?? with a visible default, not zero
        self.assertFalse(any("Tests" in f for f, _ in found))

    def test_story_a_change_is_judged_on_the_catch_it_wrote(self):
        # Given a merge request that only edits the body of the insurer's fallback
        tmp = Path(tempfile.mkdtemp(prefix="silent diff "))
        try:
            diff = tmp / "change.diff"
            diff.write_text("diff --git a/svc/src/InsurerClient.cs b/svc/src/InsurerClient.cs\n"
                            "--- a/svc/src/InsurerClient.cs\n+++ b/svc/src/InsurerClient.cs\n"
                            "@@ -14 +14 @@\n-            return 0;\n+            return 0m;\n", encoding="utf-8")
            found = scan(FIXTURE, "--diff", diff)
            # Then the catch it edited is `changed`, the other catch in that file `touched`, and the rest left out
            self.assertEqual({("src/InsurerClient.cs", 12): "changed", ("src/InsurerClient.cs", 24): "touched"},
                             {k: f["change"] for k, f in found.items()})
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


class Production(unittest.TestCase):
    def test_a_windows_checkout_with_arabic_printed_to_a_cp1252_console(self):
        tmp = Path(tempfile.mkdtemp(prefix="silent win "))
        try:
            src = tmp / "src"
            src.mkdir()
            code = ("namespace N;\r\npublic sealed class Sms(ISender sender)\r\n{\r\n    public void Remind(int id)\r\n    {\r\n"
                    "        _ = sender.SendAsync(id, \"تذكير بموعدك غدًا\");\r\n        try { sender.Flush(); }\r\n        catch { }\r\n    }\r\n}\r\n")
            (src / "Sms.cs").write_bytes("﻿".encode() + code.encode("utf-8"))
            out = subprocess.run([sys.executable, str(SCRIPT), str(tmp)], capture_output=True,
                                 env=dict(os.environ, PYTHONIOENCODING="cp1252"))
            text = out.stdout.decode("utf-8")
            self.assertEqual(0, out.returncode, out.stderr.decode("utf-8", "replace"))
            self.assertIn("| not awaited | `src/Sms.cs:6` |", text)
            self.assertIn("تذكير بموعدك", text)
            self.assertIn("| swallowed | `src/Sms.cs:8` |", text)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_a_diff_written_by_windows_powershell(self):
        tmp = Path(tempfile.mkdtemp(prefix="silent ps "))
        try:
            diff = tmp / "change.diff"
            # PowerShell 5.1's `>` writes UTF-16 with a byte-order mark and CRLF
            diff.write_bytes("+++ b/src/LabResults.cs\r\n@@ -8 +8 @@\r\n".encode("utf-16"))
            found = scan(FIXTURE, "--diff", diff)
            self.assertEqual("changed", found[("src/LabResults.cs", 8)]["change"])
            self.assertEqual("touched", found[("src/LabResults.cs", 13)]["change"])
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
