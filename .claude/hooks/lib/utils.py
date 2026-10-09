"""
Shared utilities for proven-dotnet hook scripts. Cross-platform (Windows, macOS, Linux).

Python port of lib/utils.js. Ported because node.exe is a ~110MB binary that this
machine's scanner re-scans on every launch (~3,000ms, never once fast across six
runs) while python averages ~150ms. Hooks fire constantly, so the runtime cost
dominates the work they actually do.

Behaviour is a faithful port — same env vars, same file locations, same formats —
so a session that started under the Node hooks stays readable by these.
"""

import json
import os
import sys
from datetime import datetime, timezone

CLAUDE_HOME = os.path.join(os.path.expanduser('~'), '.claude')
SESSIONS_DIR = os.path.join(CLAUDE_HOME, 'proven', 'sessions')

# Hook profile: minimal | standard | strict
HOOK_PROFILE = os.environ.get('PROVEN_HOOK_PROFILE') or 'standard'

# Disabled hooks (comma-separated IDs)
DISABLED_HOOKS = [h.strip() for h in (os.environ.get('PROVEN_DISABLED_HOOKS') or '').split(',') if h.strip()]

try:
    COMPACT_THRESHOLD = int(os.environ.get('COMPACT_THRESHOLD') or '50')
except ValueError:
    COMPACT_THRESHOLD = 50


def _iso_now():
    """ISO-8601 with a trailing Z, matching the JS Date().toISOString() format."""
    return datetime.now(timezone.utc).isoformat(timespec='milliseconds').replace('+00:00', 'Z')


def should_run(hook_id, required_profiles):
    """True if this hook is enabled and the active profile allows it."""
    if hook_id in DISABLED_HOOKS:
        return False
    return HOOK_PROFILE in required_profiles


def ensure_dir(dir_path):
    if dir_path and not os.path.isdir(dir_path):
        os.makedirs(dir_path, exist_ok=True)


def get_session_path(suffix='state'):
    """Session file for today: ~/.claude/sessions/YYYY-MM-DD-<suffix>.json"""
    ensure_dir(SESSIONS_DIR)
    date = _iso_now().split('T')[0]
    return os.path.join(SESSIONS_DIR, f'{date}-{suffix}.json')


def read_json(file_path):
    """Read JSON, returning None if missing or invalid."""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    except Exception:
        return None


def write_json(file_path, data):
    ensure_dir(os.path.dirname(file_path))
    with open(file_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)


def append_log(file_path, message):
    ensure_dir(os.path.dirname(file_path))
    with open(file_path, 'a', encoding='utf-8') as f:
        f.write(f'[{_iso_now()}] {message}\n')


def read_stdin():
    try:
        return sys.stdin.read()
    except Exception:
        return ''


def warn(script, message):
    """Hooks must never block the workflow — report and carry on."""
    try:
        sys.stderr.write(f'[proven] {script} warning: {message}\n')
    except Exception:
        pass
