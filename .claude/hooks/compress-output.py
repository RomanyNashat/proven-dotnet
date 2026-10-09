#!/usr/bin/env python3
"""
proven-dotnet output compression — trim bulky command output before Claude sees it.

OFF by default (PROVEN_COMPRESS_OUTPUT=1 to enable). Python port: this is a
PostToolUse:Bash hook, so it fires on every shell call — the one place a ~3s
Node launch was pure waste, since the hook mostly just checks a variable and exits.

Safety rule: when in doubt, pass output through. A hook that hides a real error is
far worse than one that saves no tokens.
"""

import json
import re
import sys

MIN_LINES_TO_ACT = 40
HEAD_KEEP = 6
TAIL_KEEP = 12

NOISY = [re.compile(p) for p in [
    r'\bdotnet\s+build\b', r'\bdotnet\s+test\b', r'\bdotnet\s+restore\b',
    r'\bdotnet\s+publish\b', r'\bdotnet\s+list\s+package\b',
    r'\bdotnet\s+sonarscanner\b', r'\bmsbuild\b',
    r'\bnpm\s+(install|ci|run\s+build)\b',
]]

# Lines that must always survive.
KEEP = [re.compile(p, re.I) for p in [
    r'\berror\b', r'\bwarning\b', r'\bfailed\b', r'\bfailure\b',
    r'\bexception\b', r'\bvulnerab', r'Passed!|Failed!',
    r'Total tests|Passed:|Failed:|Skipped:|Total time',
    r'Build succeeded|Build FAILED',
    r'\bNU\d{4}\b',    # NuGet, incl. NU1901-1904 vulnerability codes
    r'\bCS\d{4}\b',    # C# compiler
]]


def main():
    import os
    if os.environ.get('PROVEN_COMPRESS_OUTPUT') != '1':
        return

    try:
        data = json.loads(sys.stdin.read() or '{}')
    except Exception:
        return

    cmd = (data.get('tool_input') or {}).get('command') or ''
    resp = data.get('tool_response') or {}
    out = resp.get('stdout') or resp.get('output') or ''
    if not cmd or not out:
        return
    if not any(r.search(cmd) for r in NOISY):
        return

    lines = out.splitlines()
    if len(lines) < MIN_LINES_TO_ACT:
        return

    head, tail = lines[:HEAD_KEEP], lines[-TAIL_KEEP:]
    middle = lines[HEAD_KEEP:len(lines) - TAIL_KEEP]

    kept, dropped = [], 0
    for l in middle:
        if any(r.search(l) for r in KEEP):
            kept.append(l)
        elif l.strip():
            dropped += 1

    if dropped < 20:
        return  # little to gain; pass through

    parts = head + ['', f'… [proven trimmed {dropped} routine lines — every error, '
                        'warning and failure is kept below] …', ''] + kept + [''] + tail

    sys.stdout.write(json.dumps({
        'hookSpecificOutput': {
            'hookEventName': 'PostToolUse',
            'additionalContext': '\n'.join(parts),
        }
    }))


if __name__ == '__main__':
    main()
