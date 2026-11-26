#!/usr/bin/env python3
"""SessionStart hook — loads previous session state to maintain continuity, and in a .NET repo asks for the
one test run that lists the tests already red (known-failures.py). It runs in line, not async, so that
note reaches the session as context.
Profile: minimal, standard, strict (runs in all profiles)."""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
from utils import (  # noqa: E402
    should_run, get_session_path, read_json, write_json, append_log,
    SESSIONS_DIR, _iso_now, warn,
)


def note_known_failures(cwd):
    import importlib.util
    import json
    spec = importlib.util.spec_from_file_location(
        'known_failures', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'known-failures.py'))
    known = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(known)
    note = known.start_note(cwd)
    if note:
        print(json.dumps({'hookSpecificOutput': {'hookEventName': 'SessionStart', 'additionalContext': note}}))


def main():
    if not should_run('session:start', ['minimal', 'standard', 'strict']):
        return

    try:
        # SessionStart also fires after compaction (source="compact"). That is not a new
        # session — resetting startedAt/toolCallCount there would be wrong. post-compact.py
        # owns that case.
        try:
            import json as _json
            payload = _json.loads(sys.stdin.read() or '{}')
        except Exception:
            payload = {}
        if payload.get('source') == 'compact':
            return

        state_path = get_session_path('state')
        previous = read_json(state_path)

        # Surface the previous checkpoint on stderr, where Claude can see it.
        if previous and previous.get('checkpoint'):
            cp = previous['checkpoint']
            sys.stderr.write('[proven] Previous session checkpoint found:\n')
            for key, label in (('currentTask', 'Task'), ('nextSteps', 'Next'),
                               ('openQuestions', 'Open')):
                if cp.get(key):
                    sys.stderr.write(f'  {label}: {cp[key]}\n')

        new_state = {
            'startedAt': _iso_now(),
            'toolCallCount': 0,
            'previousCheckpoint': (previous or {}).get('checkpoint'),
        }
        # Never drop the pre-compact snapshot: post-compact.py reads it.
        if previous and previous.get('preCompactSnapshot'):
            new_state['preCompactSnapshot'] = previous['preCompactSnapshot']
        write_json(state_path, new_state)
        note_known_failures(payload.get('cwd') or os.getcwd())
    except Exception as e:
        warn('session-start', e)  # non-blocking: the session starts regardless


if __name__ == '__main__':
    main()
