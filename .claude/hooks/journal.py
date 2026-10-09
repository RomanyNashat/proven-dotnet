#!/usr/bin/env python3
"""
proven-dotnet work journal — the capture half. (Python port of journal.js.)

Ported to Python because node.exe is a ~110MB binary that this machine's scanner
re-scans on every launch: measured 2,921-4,073ms for `node -e "0"` across 6 runs,
never once fast, versus ~61-153ms for `python -c pass`. This hook fires on every
message AND every response, so it was the most expensive one to leave on Node.

Hybrid design (unchanged):
  - THIS HOOK is the safety net. It never misses a turn, but it cannot judge, so it
    writes bounded RAW entries to .claude/.journal-raw.md
  - CLAUDE distils. Per the rule in CLAUDE.md it appends the useful entry to
    .claude/journal.md. If it forgets, the raw capture still has the turn.

Usage:
  python journal.py prompt     (UserPromptSubmit)  -> what was asked
  python journal.py response   (Stop)              -> that a reply landed + files touched

Both journal files are git-ignored, and this script ENFORCES that on every write:
the file sits in a service repo on a regulated platform, so an accidental commit
would push conversation context into an MR.

Masking: a prompt often carries a value pasted while debugging (a national ID, a
mobile number, a connection string). The prompt is masked before it is written, and
on each reply both journal files are masked in place, only when something
matches (so entries from before masking existed are cleaned too). Long digit runs, emails, secret values after password=/token=/… and bearer
tokens/JWTs. A net, not a guarantee: names and free-text details are not caught.
"""

import json
import os
import re
import subprocess
import sys
from datetime import datetime

MAX_PROMPT_CHARS = 600        # bounded: a safety net, not a transcript
MAX_RAW_BYTES = 256 * 1024    # rotate rather than grow without limit
MAX_JOURNAL_BYTES = 1024 * 1024  # don't rewrite a journal bigger than this on every reply

# Same net as handover/scripts/extract_sessions.py, plus secrets.
SECRET_VALUE = re.compile(
    r'(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret)'
    r'(\s*[=:]\s*)("[^"]*"|\'[^\']*\'|[^\s;,"\']+)')
BEARER = re.compile(r'(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{16,}')
JWT = re.compile(r'\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{5,}')
EMAIL = re.compile(r'\b[\w.+-]+@[\w-]+\.[\w.-]+\b')
LONG_NUMBER = re.compile(r'\+?\b\d{9,}\b')


def mask(text):
    """Replace values that must never sit in a journal. Idempotent."""
    text = SECRET_VALUE.sub(lambda m: m.group(1) + m.group(2) + '[secret]', text)
    text = BEARER.sub('Bearer [token]', text)
    text = JWT.sub('[token]', text)
    text = EMAIL.sub('[email]', text)
    return LONG_NUMBER.sub('[number]', text)


def mask_journals(claude_dir):
    """Mask both journals (and the rotated raw file) in place, only when something matches.
    Catches entries written before masking existed, and anything typed into journal.md."""
    for name in ('journal.md', '.journal-raw.md', '.journal-raw.md.1'):
        mask_file(os.path.join(claude_dir, name))


def mask_file(path):
    try:
        if os.path.getsize(path) > MAX_JOURNAL_BYTES:
            return
        with open(path, 'r', encoding='utf-8') as f:
            text = f.read()
    except (OSError, UnicodeDecodeError):
        return
    masked = mask(text)
    if masked == text:
        return
    tmp = path + '.tmp'
    try:
        with open(tmp, 'w', encoding='utf-8') as f:
            f.write(masked)
        os.replace(tmp, path)
    except OSError:
        pass


def enforce_gitignore(repo_root):
    """Make sure both journal files can never be committed. Idempotent."""
    gi = os.path.join(repo_root, '.gitignore')
    entries = ['.claude/journal.md', '.claude/.journal-raw.md']
    current = ''
    try:
        with open(gi, 'r', encoding='utf-8') as f:
            current = f.read()
    except OSError:
        pass

    existing = {line.strip() for line in current.splitlines()}
    missing = [e for e in entries if e not in existing]
    if not missing:
        return

    block = ''
    if current and not current.endswith('\n'):
        block += '\n'
    block += '\n# proven-dotnet work journal — conversation context, never commit\n'
    block += '\n'.join(missing) + '\n'
    try:
        with open(gi, 'a', encoding='utf-8') as f:
            f.write(block)
    except OSError:
        pass  # read-only tree: skip quietly


def rotate_if_large(path):
    try:
        if os.path.getsize(path) > MAX_RAW_BYTES:
            os.replace(path, path + '.1')
    except OSError:
        pass  # not there yet


def short_stamp():
    return datetime.now().isoformat(sep=' ', timespec='minutes')


def changed_files(cwd):
    try:
        out = subprocess.run(
            ['git', 'diff', '--name-only', 'HEAD'],
            cwd=cwd, capture_output=True, text=True, timeout=2,
        ).stdout.strip()
    except Exception:
        return None
    if not out:
        return None
    files = [f for f in out.splitlines() if f]
    shown = ', '.join(files[:8])
    return f'{shown} (+{len(files) - 8} more)' if len(files) > 8 else shown


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'prompt'

    try:
        raw_in = sys.stdin.read()
    except Exception:
        return
    try:
        data = json.loads(raw_in) if raw_in.strip() else {}
    except ValueError:
        data = {}  # keep going on bad JSON

    cwd = data.get('cwd') or os.getcwd()
    claude_dir = os.path.join(cwd, '.claude')
    if not os.path.isdir(claude_dir):
        try:
            os.makedirs(claude_dir, exist_ok=True)
        except OSError:
            return

    enforce_gitignore(cwd)

    raw_path = os.path.join(claude_dir, '.journal-raw.md')
    rotate_if_large(raw_path)

    if mode == 'prompt':
        prompt = mask(' '.join((data.get('prompt') or '').split()))
        if not prompt:
            return
        if len(prompt) > MAX_PROMPT_CHARS:
            prompt = prompt[:MAX_PROMPT_CHARS] + ' …[truncated]'
        entry = f'\n- **{short_stamp()} — asked:** {prompt}\n'
    else:
        mask_journals(claude_dir)
        files = changed_files(cwd)
        entry = f'- **{short_stamp()} — replied.**'
        entry += f' Files touched: {files}\n' if files else '\n'

    try:
        with open(raw_path, 'a', encoding='utf-8') as f:
            f.write(entry)
    except OSError:
        pass  # never break the session


if __name__ == '__main__':
    main()
