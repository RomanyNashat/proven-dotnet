#!/usr/bin/env python3
"""
Stop hook — runs every end-of-turn task in ONE process.

Measured by /doctor on the work machine: python cold-start is 0.8-3.5s per launch
because endpoint security scans every new process. Three separate Stop hooks meant
three launches, up to ~10s at the end of every turn. This runs the same three tasks
in a single launch.

Each task keeps its own module and its own main(); this only multiplexes stdin, since
each one reads the hook payload from it. A failure in one never stops the others —
hooks must not break the session.
"""

import importlib
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, 'lib'))

TASKS = [
    ('evaluate-session', []),
    ('session-end', []),
    ('journal', ['response']),
]


def main():
    try:
        payload = sys.stdin.read()
    except Exception:
        payload = ''

    real_stdin, real_argv = sys.stdin, sys.argv
    for module_name, args in TASKS:
        try:
            spec = importlib.util.spec_from_file_location(
                module_name.replace('-', '_'), os.path.join(HERE, module_name + '.py'))
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)
            sys.stdin = io.StringIO(payload)       # each task gets the same payload
            sys.argv = [module_name + '.py'] + args
            mod.main()
        except Exception as e:
            try:
                sys.stderr.write(f'[proven] stop.py: {module_name} failed: {e}\n')
            except Exception:
                pass
        finally:
            sys.stdin, sys.argv = real_stdin, real_argv


if __name__ == '__main__':
    import importlib.util  # noqa: F401  (explicit for older Pythons)
    main()
