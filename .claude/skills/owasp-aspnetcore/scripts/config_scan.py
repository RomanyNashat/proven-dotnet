#!/usr/bin/env python3
"""Reads a repo's Claude config for what it could run, or tell the model, before the folder is trusted.

    python config_scan.py --repo .             # a table, high first; exit 1 when anything is high
    python config_scan.py --repo . --json      # the findings as JSON
    python config_scan.py --repo . --fail-on medium

What it reads (it only reads):
- `.claude/settings.json` and `.claude/settings.local.json`: hooks that download and run code, run encoded
  commands or a package from a registry; allow rules that let any command through (`Bash(*)`, `bash -c`,
  `python -c`, `node -e`, `curl | sh`); `bypassPermissions`; every project MCP server switched on.
- `.mcp.json`: a server installed unpinned (`npx -y pkg`), a secret written in the file, plain http.
- `CLAUDE.md`, `AGENTS.md` and the `.md` files under `.claude/`: hidden characters (zero-width, right-to-left
  overrides, tag characters) and text that orders the model ("ignore previous instructions", "don't tell the
  user"). A phrase in quotes or backticks is a quotation, not an order, and is left alone.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class Finding:
    severity: str
    rule: str
    file: str
    line: int
    what: str
    fix: str


SHELLS = r"(?:sh|bash|zsh|dash|python3?|node|pwsh|powershell(?:\.exe)?|iex|Invoke-Expression|cmd(?:\.exe)?)"
DOWNLOAD = re.compile(rf"\b(?:curl|wget|iwr|irm|Invoke-WebRequest|Invoke-RestMethod)\b[^|;&]*\|\s*(?:sudo\s+)?{SHELLS}\b"
                      r"|\$\((?:curl|wget)\b|\biex\s*\(.*(?:DownloadString|iwr|irm|Invoke-WebRequest)", re.I)
ENCODED = re.compile(r"base64\s+(?:-d|--decode)\b.*\|\s*" + SHELLS + r"|\bpowershell(?:\.exe)?\b.*\s-e(?:nc|ncodedcommand)?\s+[A-Za-z0-9+/=]{8,}"
                     r"|FromBase64String|\beval\s+\"?\$\(.*base64", re.I)
PACKAGE_RUNNER = re.compile(r"\b(?:npx|uvx|bunx|pnpm\s+dlx|pipx\s+run)\b", re.I)
RUNS_ANYTHING = [
    re.compile(r"^(?:Bash|PowerShell)\(\s*\*?\s*\)$"),
    re.compile(r"^(?:Bash|PowerShell)\(\s*(?:sudo\s+)?(?:bash|sh|zsh|pwsh|powershell|cmd)(?:\.exe)?\s+(?:-c|/c|-command|-encodedcommand)\b", re.I),
    re.compile(r"^(?:Bash|PowerShell)\(\s*python3?(?:\.exe)?\s+(?:-c|-)(?:\s|\*|\))", re.I),
    re.compile(r"^(?:Bash|PowerShell)\(\s*node(?:\.exe)?\s+(?:-e|--eval|-p)\b", re.I),
    re.compile(r"^(?:Bash|PowerShell)\(\s*(?:eval|iex|Invoke-Expression|sudo)\b", re.I),
    re.compile(r"^(?:Bash|PowerShell)\(.*\b(?:curl|wget|iwr|irm)\b.*\|", re.I),
]
SECRET = re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|glpat-[A-Za-z0-9_-]{20,}|xox[abpr]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}"
                    r"|sk-[A-Za-z0-9_-]{20,}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})\b|Bearer\s+[A-Za-z0-9._-]{20,}")
HIDDEN = re.compile("[​-‍⁠‪-‮⁦-⁩]|[\U000e0000-\U000e007f]|(?<!^)﻿")
ORDERS = re.compile(
    r"\b(?:ignore|disregard|forget)\s+(?:all\s+|any\s+)?(?:the\s+)?(?:previous|prior|above|earlier|your)\s+(?:instructions|rules|guidelines)"
    r"|\b(?:don't|do not)\s+(?:tell|inform|show|mention\s+(?:it\s+)?to)\s+the\s+(?:user|developer)\b"
    r"|\bnever\s+(?:tell|inform|show)\s+the\s+(?:user|developer)\s+(?:that\s+you|you|about|what\s+you)\b"
    r"|\bwithout\s+(?:asking|telling|showing)\s+the\s+(?:user|developer)"
    r"|\b(?:disable|turn\s+off|bypass|skip)\s+(?:the\s+|all\s+)?(?:hooks?|guards?|sandbox|permission\s+prompts?|security\s+checks?)"
    r"|\b(?:approve|merge)\s+this\s+(?:merge\s+request|MR|PR|pull\s+request)"
    r"|\byou\s+are\s+now\s+(?:in\s+)?(?:developer|admin|unrestricted|jailbreak)", re.I)
TO_THE_MODEL = re.compile(r"<!--[^>]*\b(?:AI|Claude|assistant|model|LLM)\s*:", re.I)
QUOTED = re.compile(r"\"[^\"\n]*\"|“[^”\n]*”|`[^`\n]*`|'[^'\n]{6,}'")


def line_of(text: str, needle: str) -> int:
    probe = needle[:40]
    for i, line in enumerate(text.splitlines(), 1):
        if probe and probe in line:
            return i
    return 1


def load(path: Path) -> tuple[dict | None, str]:
    try:
        text = path.read_text(encoding="utf-8-sig")
        return json.loads(text), text
    except (OSError, ValueError):
        return None, ""


def scan_settings(repo: Path) -> list[Finding]:
    found = []
    for rel in (".claude/settings.json", ".claude/settings.local.json"):
        data, text = load(repo / rel)
        if not isinstance(data, dict):
            continue
        for event, matchers in (data.get("hooks") or {}).items():
            for m in matchers if isinstance(matchers, list) else []:
                for h in (m.get("hooks") or []) if isinstance(m, dict) else []:
                    command = str(h.get("command") or "")
                    escaped = json.dumps(command)[1:-1]
                    at = line_of(text, escaped)
                    if DOWNLOAD.search(command):
                        found.append(Finding("high", "hook-runs-download", rel, at, f"{event} hook: {command[:120]}",
                                             "remove it, or vendor the script into the repo where it can be read"))
                    elif ENCODED.search(command):
                        found.append(Finding("high", "hook-runs-encoded", rel, at, f"{event} hook: {command[:120]}",
                                             "remove it: a hook nobody can read is not reviewed"))
                    elif PACKAGE_RUNNER.search(command):
                        found.append(Finding("medium", "hook-runs-package", rel, at, f"{event} hook: {command[:120]}",
                                             "pin the version, or run a script kept in the repo"))
                    if event == "SessionStart":
                        found.append(Finding("info", "session-start-hook", rel, at, f"runs on opening the folder: {command[:120]}",
                                             "read it before trusting the folder"))
        permissions = data.get("permissions") or {}
        for rule in permissions.get("allow") or []:
            if any(p.search(str(rule).strip()) for p in RUNS_ANYTHING):
                found.append(Finding("high", "allow-runs-anything", rel, line_of(text, json.dumps(rule)[1:-1]), str(rule),
                                     "remove the rule: it lets any command run without a prompt"))
        if permissions.get("defaultMode") == "bypassPermissions":
            found.append(Finding("high", "bypass-permissions", rel, line_of(text, "bypassPermissions"),
                                 "defaultMode: bypassPermissions", "remove it: nothing asks first in this mode"))
        if data.get("enableAllProjectMcpServers") is True:
            found.append(Finding("medium", "all-mcp-servers-on", rel, line_of(text, "enableAllProjectMcpServers"),
                                 "every server in .mcp.json starts without being approved", "approve the servers one by one"))
    return found


def unpinned(command: str, args: list[str]) -> bool:
    name = Path(command).name.lower().removesuffix(".exe").removesuffix(".cmd")
    rest = [a for a in args if not a.startswith("-")]
    if name in ("npx", "bunx", "pnpm") and rest:
        package = rest[1] if name == "pnpm" and rest[0] == "dlx" and len(rest) > 1 else rest[0]
        return not re.search(r"(?<!^)@\d", package)
    if name in ("uvx", "pipx") and rest:
        package = rest[1] if name == "pipx" and rest[0] == "run" and len(rest) > 1 else rest[0]
        return "==" not in package and not re.search(r"@\d", package)
    if name == "docker" and "run" in args:
        image = next((a for a in args[args.index("run") + 1:] if not a.startswith("-") and "=" not in a), "")
        return bool(image) and "@sha256:" not in image and not re.search(r":[\w.-]+$", image.split("/")[-1])
    return False


def scan_mcp(repo: Path) -> list[Finding]:
    found = []
    for rel in (".mcp.json", ".claude/.mcp.json"):
        data, text = load(repo / rel)
        servers = (data or {}).get("mcpServers") or {}
        for name, server in servers.items() if isinstance(servers, dict) else []:
            if not isinstance(server, dict):
                continue
            at = line_of(text, json.dumps(name))
            command, args = str(server.get("command") or ""), [str(a) for a in server.get("args") or []]
            if command and unpinned(command, args):
                found.append(Finding("medium", "mcp-unpinned", rel, at, f"{name}: {command} {' '.join(args)}"[:160],
                                     "pin the version (pkg@1.2.3, image@sha256:...): unpinned, it installs whatever is newest"))
            blob = json.dumps({k: server.get(k) for k in ("env", "headers", "args", "url")})
            if SECRET.search(blob):
                found.append(Finding("high", "mcp-secret-inline", rel, at, f"{name}: a token is written in the file",
                                     "move it to an environment variable the file names, and rotate it"))
            url = str(server.get("url") or "")
            if url.startswith("http://") and not re.match(r"http://(?:localhost|127\.0\.0\.1|\[::1\])(?:[:/]|$)", url):
                found.append(Finding("medium", "mcp-plain-http", rel, at, f"{name}: {url}", "use https"))
    return found


def instruction_files(repo: Path) -> list[Path]:
    files = [p for p in (repo / "CLAUDE.md", repo / "AGENTS.md", repo / "CLAUDE.local.md") if p.is_file()]
    claude = repo / ".claude"
    if claude.is_dir():
        files += sorted(p for p in claude.rglob("*.md") if not any(part in ("node_modules",) for part in p.parts))
    return files


def scan_text(repo: Path) -> list[Finding]:
    found = []
    for path in instruction_files(repo):
        rel = path.relative_to(repo).as_posix()
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            if HIDDEN.search(line if i > 1 else line.lstrip("﻿")):
                codes = sorted({f"U+{ord(c):04X}" for c in HIDDEN.findall(line)})
                found.append(Finding("high", "hidden-characters", rel, i, f"{', '.join(codes)} in: {visible(line)[:100]}",
                                     "delete them: they hide or reorder text a person reads"))
            bare = QUOTED.sub(" ", line)
            if TO_THE_MODEL.search(line) or ORDERS.search(bare):
                found.append(Finding("high", "instruction-to-the-model", rel, i, visible(line)[:140],
                                     "remove it, and find who added it: text that orders the model is an attack on the review"))
    return found


def visible(line: str) -> str:
    return HIDDEN.sub(lambda m: f"<U+{ord(m.group()):04X}>", line).strip()


ORDER = {"high": 0, "medium": 1, "info": 2}


def scan(repo: Path) -> list[Finding]:
    findings = scan_settings(repo) + scan_mcp(repo) + scan_text(repo)
    return sorted(findings, key=lambda f: (ORDER[f.severity], f.file, f.line))


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", default=".")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--fail-on", choices=("high", "medium"), default="high")
    args = ap.parse_args(argv)
    findings = scan(Path(args.repo))
    if args.json:
        print(json.dumps([asdict(f) for f in findings], ensure_ascii=False, indent=1))
    elif not findings:
        print("Claude config: nothing found.")
    else:
        print("| Severity | Where | What | Fix |\n|---|---|---|---|")
        for f in findings:
            print(f"| {f.severity} | {f.file}:{f.line} | {f.rule}: {f.what.replace('|', '/')} | {f.fix} |")
    worst = {"high": ("high",), "medium": ("high", "medium")}[args.fail_on]
    return 1 if any(f.severity in worst for f in findings) else 0


if __name__ == "__main__":
    sys.exit(main())
