#!/usr/bin/env python3
"""Stop hook — evaluates the session and extracts patterns for continuous
learning. Profile: standard, strict. Fires at the end of every response."""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
from utils import (  # noqa: E402
    should_run, get_session_path, read_json, write_json, append_log,
    SESSIONS_DIR, _iso_now, warn,
)

import json


def main():
    if not should_run('stop:evaluate', ['standard', 'strict']):
        return

    try:
        state_path = get_session_path('state')
        state = read_json(state_path) or {}

        snapshot = state.get('preCompactSnapshot') or {}
        files = snapshot.get('modifiedFiles') or []
        calls = state.get('toolCallCount') or 0
        duration = state.get('durationMinutes')

        evaluation = {
            'evaluatedAt': _iso_now(),
            'sessionDuration': duration,
            'toolCallCount': calls,
            'modifiedFiles': files,
            'compactSuggested': calls > 50,
            'metrics': {
                'filesModified': len(files),
                'avgToolCallsPerMinute': (
                    round(calls / duration, 1) if duration else None),
            },
        }

        # Patterns inferred from what was touched.
        checks = [
            ('tests-written',           lambda f: 'Test' in f),
            ('migration-created',       lambda f: 'Migration' in f),
            ('domain-modified',         lambda f: f.endswith('.cs') and 'Domain' in f),
            ('infrastructure-modified', lambda f: f.endswith('.cs') and 'Infrastructure' in f),
            ('docker-modified',         lambda f: 'Dockerfile' in f or 'docker' in f),
            ('pipeline-modified',       lambda f: f.endswith('.yml') or f.endswith('.yaml')),
        ]
        evaluation['patterns'] = [name for name, pred in checks if any(pred(f) for f in files)]

        state['evaluation'] = evaluation
        write_json(state_path, state)

        # Learning log is JSONL — one evaluation per line.
        try:
            with open(os.path.join(SESSIONS_DIR, 'learning-log.jsonl'), 'a',
                      encoding='utf-8') as f:
                f.write(json.dumps(evaluation) + '\n')
        except OSError:
            pass
    except Exception as e:
        warn('evaluate-session', e)


if __name__ == '__main__':
    main()
