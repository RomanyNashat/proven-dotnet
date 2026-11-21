#!/usr/bin/env python3
"""
PostToolUse:Edit|Write|MultiEdit — run `dotnet format` on an edited C# file, and record the edit for the
done gate (tdd-gate.py), in this one launch: a second hook on every edit would start Python twice.
Python port: node.exe costs ~3s per launch on the work machine; this fires on
every edit. Behaviour is unchanged, including "never block the workflow".
"""

import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

PROFILES = ('standard', 'strict')


def should_run():
    profile = os.environ.get('PROVEN_HOOK_PROFILE', 'standard')
    return profile in PROFILES


def record_for_done_gate(event):
    """The done gate's record of an edit; never stops the format, whatever goes wrong."""
    try:
        spec = importlib.util.spec_from_file_location("tdd_gate", Path(__file__).resolve().parent / "tdd-gate.py")
        gate = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(gate)
        path = gate.state_path(event)
        state = gate.load(path)
        gate.post(event, state)
        gate.save(path, state)
    except Exception:
        pass


def main():
    try:
        raw = sys.stdin.read()
    except Exception:
        return
    try:
        event = json.loads(raw) if raw.strip() else {}
    except ValueError:
        event = None
    if isinstance(event, dict) and event.get('tool_name'):
        record_for_done_gate(event)

    if not should_run():
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
