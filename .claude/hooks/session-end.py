#!/usr/bin/env python3
"""Stop hook — persists final session state and a completion marker.
Profile: minimal, standard, strict. NOTE: Stop fires at the end of EVERY
response, not once per session, which is why the runtime cost mattered."""

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lib'))
from utils import (  # noqa: E402
    should_run, get_session_path, read_json, write_json, append_log,
    SESSIONS_DIR, _iso_now, warn,
)

from datetime import datetime


def main():
    if not should_run('stop:session-end', ['minimal', 'standard', 'strict']):
        return

    try:
        state_path = get_session_path('state')
        state = read_json(state_path) or {}

        state['endedAt'] = _iso_now()
        state['completed'] = True

        started = state.get('startedAt')
        if started:
            try:
                a = datetime.fromisoformat(started.replace('Z', '+00:00'))
                b = datetime.fromisoformat(state['endedAt'].replace('Z', '+00:00'))
                state['durationMinutes'] = round((b - a).total_seconds() / 60)
            except ValueError:
                pass

        write_json(state_path, state)

        append_log(
            os.path.join(SESSIONS_DIR, 'session-log.txt'),
            'Session ended | Duration: {}min | Tool calls: {}'.format(
                state.get('durationMinutes', '?'), state.get('toolCallCount', 0)),
        )
    except Exception as e:
        warn('session-end', e)


if __name__ == '__main__':
    main()
