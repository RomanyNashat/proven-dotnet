#!/usr/bin/env python3
"""
PostToolUse:Edit|Write — run `dotnet format` on an edited C# file.
Python port: node.exe costs ~3s per launch on the work machine; this fires on
every edit. Behaviour is unchanged, including "never block the workflow".
"""

import json
import os
import re
import subprocess
import sys

PROFILES = ('standard', 'strict')


def should_run():
    profile = os.environ.get('PROVEN_HOOK_PROFILE', 'standard')
    return profile in PROFILES


def main():
    if not should_run():
        return

    try:
        raw = sys.stdin.read()
    except Exception:
        return

    file_path = ''
    try:
        parsed = json.loads(raw) if raw.strip() else {}
        file_path = (parsed.get('file_path') or parsed.get('filePath')
                     or parsed.get('path') or '')
        if not file_path:
            ti = parsed.get('tool_input') or {}
            file_path = ti.get('file_path') or ti.get('path') or ''
    except ValueError:
        m = re.search(r'(?:file_path|path)["\']?\s*[:=]\s*["\']?([^\s"\',}]+\.cs)', raw, re.I)
        if m:
            file_path = m.group(1)

    if not file_path or not file_path.endswith('.cs'):
        return

    resolved = os.path.abspath(file_path)
    try:
        subprocess.run(
            ['dotnet', 'format', '--include', resolved, '--no-restore'],
            capture_output=True, timeout=10,
        )
    except Exception:
        # dotnet format failure is non-blocking — commonly "file not in a project"
        pass


if __name__ == '__main__':
    main()
