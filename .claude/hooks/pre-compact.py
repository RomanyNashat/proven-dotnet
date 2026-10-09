#!/usr/bin/env python3
"""PreCompact hook — saves critical state before compaction wipes history.
Profile: standard, strict."""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
from utils import (  # noqa: E402
    should_run, get_session_path, read_json, write_json, append_log,
    SESSIONS_DIR, _iso_now, warn,
)

import subprocess


def _git(args):
    try:
        out = subprocess.run(['git'] + args, capture_output=True, text=True, timeout=3)
        return out.stdout.strip()
    except Exception:
        return ''  # not a git repo, or git unavailable — fine


def main():
    if not should_run('pre:compact', ['standard', 'strict']):
        return

    try:
        state_path = get_session_path('state')
        state = read_json(state_path) or {}

        modified = [f for f in _git(['diff', '--name-only']).splitlines() if f]
        branch = _git(['branch', '--show-current'])

        state['preCompactSnapshot'] = {
            'savedAt': _iso_now(),
            'toolCallCount': state.get('toolCallCount', 0),
            'modifiedFiles': modified,
            'currentBranch': branch,
        }
        write_json(state_path, state)

        sys.stderr.write(
            f'[proven] Pre-compact state saved. {len(modified)} modified files captured.\n')
    except Exception as e:
        warn('pre-compact', e)


if __name__ == '__main__':
    main()
