"""Claude's file tools are kept off the files where developers keep secrets on their own machine.

A guard against slips, not a lock: a shell process can still read these files. Rules use Claude Code's
gitignore-style paths: a bare name matches at any depth under the working folder, `~/` is the home folder
(on Windows `C:\\Users\\<you>` too, after Claude Code turns it into `/c/Users/<you>`).
"""
import fnmatch
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
DENY = json.loads((ROOT / ".claude" / "settings.json").read_text())["permissions"]["deny"]
SECRET_READS = [
    "Read(.env)",
    "Read(.env.*)",
    # dotnet user-secrets: %APPDATA%\Microsoft\UserSecrets on Windows, ~/.microsoft/usersecrets elsewhere.
    "Read(~/AppData/Roaming/Microsoft/UserSecrets/**)",
    "Read(~/.microsoft/usersecrets/**)",
]


def denied(name):
    """Whether a file name in the project is covered by one of the bare-name Read rules."""
    return any(fnmatch.fnmatch(name, rule[5:-1]) for rule in DENY if rule.startswith("Read(") and "/" not in rule)


class SecretReadsTests(unittest.TestCase):
    def test_settings_deny_reading_each_place_secrets_live(self):
        for rule in SECRET_READS:
            self.assertIn(rule, DENY)

    def test_story_a_developer_keeps_a_token_in_dotenv_and_claude_is_asked_to_check_the_config(self):
        # Given a project with a .env, a .env.production and the usual appsettings files
        files = [".env", ".env.production", "appsettings.json", "appsettings.Development.json", "Program.cs"]
        # When Claude reads its way through the config
        blocked = [f for f in files if denied(f)]
        # Then the two env files are off limits and appsettings stays readable
        self.assertEqual([".env", ".env.production"], blocked)

    def test_no_rule_blocks_appsettings_or_source(self):
        for name in ["appsettings.json", "appsettings.Development.json", "Program.cs", "docker-compose.yml"]:
            self.assertFalse(denied(name), name)


if __name__ == "__main__":
    unittest.main()
