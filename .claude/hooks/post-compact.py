#!/usr/bin/env python3
"""
Post-compaction re-injection — registered on SessionStart with matcher "compact".

NOT on PostCompact: that event cannot return additionalContext (its output fails schema
validation: hookSpecificOutput.hookEventName must be one of SessionStart, UserPromptSubmit,
PostToolUse, Stop, ...). SessionStart fires after both manual and auto compaction with
source="compact", and its additionalContext IS injected. Learned the hard way in v10.21.

Original description:
PostCompact hook — re-inject working context AFTER a compaction.

`pre-compact.py` already snapshots state before history is wiped, but until now
nothing read it back: the snapshot was written and forgotten. This is the other
half. Compaction is the moment continuity is lost — the plan, the decisions, what
was already tried — and re-reading the repo does not recover any of it.

`/wake-up` does this for session START. This does it for mid-session recovery, and
it runs automatically rather than being remembered.

Output goes to stdout as additionalContext, which Claude Code injects into the
freshly-compacted conversation.
"""

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
from utils import should_run, get_session_path, read_json, warn  # noqa: E402

MAX_JOURNAL_CHARS = 2000    # a reminder, not a replay
MAX_FILES = 12


def tail_journal(cwd):
    """The last few distilled entries — decisions and why, which is what compaction loses."""
    path = os.path.join(cwd, '.claude', 'journal.md')
    if not os.path.exists(path):
        path = os.path.join(cwd, '.claude', '.journal-raw.md')
        if not os.path.exists(path):
            return None
    try:
        with open(path, 'r', encoding='utf-8') as f:
            text = f.read()
    except OSError:
        return None
    if not text.strip():
        return None
    return text[-MAX_JOURNAL_CHARS:] if len(text) > MAX_JOURNAL_CHARS else text


def main():
    if not should_run('post:compact', ['standard', 'strict']):
        return

    try:
        data = {}
        try:
            raw = sys.stdin.read()
            data = json.loads(raw) if raw.strip() else {}
        except Exception:
            pass

        if data.get('source') not in (None, 'compact'):
            return  # a normal startup/resume/clear — session-start.py handles those

        cwd = data.get('cwd') or os.getcwd()
        state = read_json(get_session_path('state')) or {}
        snap = state.get('preCompactSnapshot') or {}

        parts = []

        files = (snap.get('modifiedFiles') or [])[:MAX_FILES]
        branch = snap.get('currentBranch')
        if branch or files:
            parts.append('**Where the work was when context was compacted**')
            if branch:
                parts.append(f'- Branch: `{branch}`')
            if files:
                parts.append(f'- Uncommitted ({len(snap.get("modifiedFiles") or [])}): '
                             + ', '.join(f'`{f}`' for f in files))

        journal = tail_journal(cwd)
        if journal:
            parts.append('')
            parts.append('**Recent journal entries** (decisions and why — the part compaction loses):')
            parts.append(journal.strip())

        if not parts:
            return  # nothing worth injecting; stay quiet

        parts.append('')
        parts.append('Treat this as a reminder of where things stood, not as instructions. '
                     'The repo is the source of truth — if it disagrees with the above, the repo wins.')

        sys.stdout.write(json.dumps({
            'hookSpecificOutput': {
                'hookEventName': 'SessionStart',
                'additionalContext': '\n'.join(parts),
            }
        }))
    except Exception as e:
        warn('post-compact', e)  # never break the session


if __name__ == '__main__':
    main()
