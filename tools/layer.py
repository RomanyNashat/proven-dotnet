#!/usr/bin/env python3
"""Installs, updates and uninstalls a layer of Claude Code files in ~/.claude without touching anything
that isn't the layer's own.

    python tools/layer.py install <layer source dir> [--claude-dir DIR]
    python tools/layer.py uninstall <layer id> [--claude-dir DIR]
    python tools/layer.py status [--claude-dir DIR]

A layer source dir holds layer.json ({"id": ...}; the version comes from ../VERSION unless layer.json
has "version") and any of:

    core.md        -> rules/<id>/core.md   (always-loaded instructions; the user's CLAUDE.md is never touched)
    rules/**       -> rules/<id>/**
    agents/*.md    -> agents/*.md
    commands/*.md  -> commands/*.md
    skills/<n>/**  -> skills/<n>/**
    hooks/**       -> hooks/<id>/**
    files/**       -> <id>/**              (anything else the layer needs: templates, scripts)
    settings.json  -> merged into settings.json, key by key

What the layer owns is recorded in .layers/registry.json. Install is also update:
  - a file is deleted only if this layer installed it before and no longer ships it;
  - a file that exists but isn't this layer's (the user's own, or another layer's) is never overwritten:
    it is skipped with a warning (for a skill, the whole skill);
  - settings.json keys are added only where the user has none; a key this layer set is updated or removed
    only while it still holds the value the layer wrote. Hook entries are this layer's when their command
    runs a script from hooks/<id>/.
Placeholders in settings.json: {HOOKS} -> ~/.claude/hooks/<id>, {FILES} -> the absolute path of <id>/
(forward slashes, which Windows accepts), {PYTHON} -> how this machine starts Python 3.
"""
from __future__ import annotations

import argparse
import copy
import datetime as _dt
import json
import os
import shutil
import sys
from pathlib import Path

REGISTRY = ".layers/registry.json"
SPECIAL_LISTS = ("allow", "ask", "deny", "additionalDirectories")


def warn(msg: str) -> None:
    print(f"warn: {msg}")


# ---------------------------------------------------------------------------------------------------
# registry


def load_registry(claude: Path) -> dict:
    path = claude / REGISTRY
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8-sig"))
    return {"layers": {}}


def save_registry(claude: Path, registry: dict) -> None:
    write_json(claude / REGISTRY, registry)


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(tmp, path)


# ---------------------------------------------------------------------------------------------------
# files


def planned_files(source: Path, layer_id: str) -> dict[str, Path]:
    """target path (posix, relative to ~/.claude) -> source file"""
    plan: dict[str, Path] = {}

    def add_tree(src_root: Path, target_prefix: str) -> None:
        if not src_root.is_dir():
            return
        for f in sorted(src_root.rglob("*")):
            if f.is_file() and "__pycache__" not in f.parts:
                plan[f"{target_prefix}/{f.relative_to(src_root).as_posix()}"] = f

    if (source / "core.md").is_file():
        plan[f"rules/{layer_id}/core.md"] = source / "core.md"
    add_tree(source / "rules", f"rules/{layer_id}")
    for kind in ("agents", "commands"):
        d = source / kind
        if d.is_dir():
            for f in sorted(d.glob("*.md")):
                plan[f"{kind}/{f.name}"] = f
    skills = source / "skills"
    if skills.is_dir():
        for skill in sorted(p for p in skills.iterdir() if p.is_dir()):
            add_tree(skill, f"skills/{skill.name}")
    add_tree(source / "hooks", f"hooks/{layer_id}")
    add_tree(source / "files", layer_id)
    return plan


def unit_of(target: str) -> str:
    """Collisions are judged per skill (a skill is one unit), per file otherwise."""
    parts = target.split("/")
    return "/".join(parts[:2]) if parts[0] == "skills" else target


def owner_of(registry: dict, target: str, unit: str) -> str | None:
    for lid, layer in registry["layers"].items():
        files = layer.get("files", [])
        if target in files or any(unit_of(f) == unit for f in files):
            return lid
    return None


def install_files(claude: Path, layer_id: str, plan: dict[str, Path], registry: dict) -> list[str]:
    previous = set(registry["layers"].get(layer_id, {}).get("files", []))
    previous_units = {unit_of(f) for f in previous}

    # 1. decide what this layer may write
    blocked_units: dict[str, str] = {}
    for target in plan:
        unit = unit_of(target)
        if unit in previous_units or unit in blocked_units:
            continue
        owner = owner_of({"layers": {k: v for k, v in registry["layers"].items() if k != layer_id}}, target, unit)
        if owner:
            blocked_units[unit] = f"belongs to the layer '{owner}'"
        elif (claude / unit).exists():
            blocked_units[unit] = "already exists and isn't from this layer (yours?)"
    for unit, why in sorted(blocked_units.items()):
        warn(f"skipped {unit}: {why}. It was left as it is.")
    installed = {t: s for t, s in plan.items() if unit_of(t) not in blocked_units}

    # 2. remove what this layer shipped before and doesn't ship now
    for target in sorted(previous - set(installed)):
        path = claude / target
        if path.is_file():
            path.unlink()
            print(f"removed {target}")
        prune_empty_dirs(claude, path.parent)

    # 3. copy
    for target, src in installed.items():
        dest = claude / target
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dest)
    return sorted(installed)


def prune_empty_dirs(claude: Path, directory: Path) -> None:
    claude = claude.resolve()
    d = directory.resolve()
    while d != claude and claude in d.parents and d.is_dir() and not any(d.iterdir()):
        d.rmdir()
        d = d.parent


# ---------------------------------------------------------------------------------------------------
# settings.json


def python_command() -> str:
    """How hooks start Python on this machine: python3 on macOS and Linux (where "python" often doesn't
    exist), python or the py launcher on Windows (never the Microsoft Store placeholder)."""
    if os.name == "nt":
        found = shutil.which("python")
        if found and "WindowsApps" not in found:
            return "python"
        return "py -3" if shutil.which("py") else "python"
    return "python3" if shutil.which("python3") else "python"


def fill_placeholders(value, layer_id: str, claude: Path):
    if isinstance(value, str):
        files = (claude / layer_id).resolve().as_posix()
        return (value.replace("{HOOKS}", f"~/.claude/hooks/{layer_id}").replace("{FILES}", files)
                .replace("{PYTHON}", python_command()))
    if isinstance(value, list):
        return [fill_placeholders(v, layer_id, claude) for v in value]
    if isinstance(value, dict):
        return {k: fill_placeholders(v, layer_id, claude) for k, v in value.items()}
    return value


def is_layer_hook(hook: dict, layer_id: str) -> bool:
    cmd = str(hook.get("command", "")).replace("\\", "/")
    return f"/hooks/{layer_id}/" in cmd


def merge_settings(claude: Path, layer_id: str, fragment: dict | None, registry: dict) -> dict:
    """Returns this layer's new ownership record {"keys": {...}, "lists": {...}}."""
    path = claude / "settings.json"
    try:
        settings = json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}
    except json.JSONDecodeError as e:
        sys.exit(f"error: {path} isn't valid JSON ({e}). Nothing was changed; fix it and run again.")
    if path.exists():
        backup_settings(claude, path)
    before = copy.deepcopy(settings)
    old = registry["layers"].get(layer_id, {}).get("settings", {"keys": {}, "lists": {}})
    others = {lid: l.get("settings", {}) for lid, l in registry["layers"].items() if lid != layer_id}
    fragment = fill_placeholders(fragment or {}, layer_id, claude)
    owned_keys: dict[str, object] = {}
    owned_lists: dict[str, list] = {}

    # hooks: replace this layer's entries, keep everyone else's
    hooks = settings.get("hooks", {})
    for event in list(hooks):
        groups = []
        for group in hooks[event]:
            kept = [h for h in group.get("hooks", []) if not is_layer_hook(h, layer_id)]
            if kept:
                groups.append({**group, "hooks": kept})
        if groups:
            hooks[event] = groups
        else:
            del hooks[event]
    for event, groups in fragment.get("hooks", {}).items():
        hooks.setdefault(event, []).extend(copy.deepcopy(groups))
    if hooks:
        settings["hooks"] = hooks
    else:
        settings.pop("hooks", None)

    # scalar-like keys: "env.X", "permissions.defaultMode", and any other top-level key as one unit
    wanted: dict[str, object] = {}
    for key, value in fragment.items():
        if key == "hooks":
            continue
        if key in ("env", "permissions") and isinstance(value, dict):
            for sub, v in value.items():
                if key == "permissions" and sub in SPECIAL_LISTS:
                    continue
                wanted[f"{key}.{sub}"] = v
        else:
            wanted[key] = value

    for key in sorted(set(old.get("keys", {})) | set(wanted)):
        current = get_path(settings, key)
        ours_before = key in old.get("keys", {})
        unchanged = ours_before and current == old["keys"][key]
        if key in wanted:
            new = wanted[key]
            if current is None or unchanged:
                set_path(settings, key, new)
                owned_keys[key] = new
            elif any(key in o.get("keys", {}) for o in others.values()):
                warn(f"settings: {key} is set by another layer; kept its value.")
            elif current != new:
                warn(f"settings: kept your {key}; this layer's value would be {json.dumps(new)}.")
        elif unchanged:
            del_path(settings, key)  # this layer no longer sets it

    # permission lists: add this layer's entries, remove the ones it added before and no longer ships
    permissions = settings.get("permissions", {})
    for name in SPECIAL_LISTS:
        new_entries = (fragment.get("permissions") or {}).get(name, [])
        old_entries = old.get("lists", {}).get(name, [])
        current = permissions.get(name, [])
        others_entries = {e for o in others.values() for e in o.get("lists", {}).get(name, [])}
        current = [e for e in current if e not in old_entries or e in new_entries or e in others_entries]
        added = [e for e in old_entries if e in new_entries and e in current]
        for e in new_entries:
            if e in current:
                continue
            if e in old_entries:
                warn(f"settings: permissions.{name} no longer has {e!r}; you removed it, so it stays out.")
                continue
            current.append(e)
            added.append(e)
        if current:
            permissions[name] = current
        else:
            permissions.pop(name, None)
        if added:
            owned_lists[name] = sorted(set(added))
    if permissions:
        settings["permissions"] = permissions
    else:
        settings.pop("permissions", None)

    if settings != before or not path.exists():
        write_json(path, settings)
    return {"keys": owned_keys, "lists": owned_lists}


def backup_settings(claude: Path, path: Path) -> None:
    backups = claude / ".layers" / "backups"
    backups.mkdir(parents=True, exist_ok=True)
    stamp = _dt.datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    shutil.copyfile(path, backups / f"settings-{stamp}.json")
    for old in sorted(backups.glob("settings-*.json"))[:-10]:
        old.unlink()


def get_path(d: dict, key: str):
    for part in key.split("."):
        if not isinstance(d, dict) or part not in d:
            return None
        d = d[part]
    return d


def set_path(d: dict, key: str, value) -> None:
    parts = key.split(".")
    for part in parts[:-1]:
        d = d.setdefault(part, {})
    d[parts[-1]] = copy.deepcopy(value)


def del_path(d: dict, key: str) -> None:
    parts = key.split(".")
    stack = [d]
    for part in parts[:-1]:
        if part not in stack[-1]:
            return
        stack.append(stack[-1][part])
    stack[-1].pop(parts[-1], None)
    for i in range(len(parts) - 1, 0, -1):  # drop parents left empty
        if not stack[i]:
            stack[i - 1].pop(parts[i - 1], None)


# ---------------------------------------------------------------------------------------------------
# commands


def install(source: Path, claude: Path) -> None:
    meta = json.loads((source / "layer.json").read_text(encoding="utf-8-sig"))
    layer_id = meta["id"]
    version = meta.get("version") or (source.parent / "VERSION").read_text(encoding="utf-8").strip()
    claude.mkdir(parents=True, exist_ok=True)
    registry = load_registry(claude)
    previous = registry["layers"].get(layer_id, {}).get("version")
    print(f"{layer_id}: {'updating ' + previous + ' -> ' if previous else 'installing '}{version}")

    files = install_files(claude, layer_id, planned_files(source, layer_id), registry)
    fragment_path = source / "settings.json"
    fragment = json.loads(fragment_path.read_text(encoding="utf-8-sig")) if fragment_path.exists() else {}
    owned = merge_settings(claude, layer_id, fragment, registry)

    registry["layers"][layer_id] = {
        "version": version,
        "installed": _dt.datetime.now(_dt.timezone.utc).isoformat(timespec="seconds"),
        "files": files,
        "settings": owned,
    }
    save_registry(claude, registry)
    print(f"{layer_id} {version}: {len(files)} files. Restart Claude Code to load them.")


def uninstall(layer_id: str, claude: Path) -> None:
    registry = load_registry(claude)
    if layer_id not in registry["layers"]:
        print(f"{layer_id} isn't installed.")
        return
    layer = registry["layers"][layer_id]
    for target in layer.get("files", []):
        path = claude / target
        if path.is_file():
            path.unlink()
        prune_empty_dirs(claude, path.parent)
    merge_settings(claude, layer_id, {}, registry)
    del registry["layers"][layer_id]
    save_registry(claude, registry)
    print(f"{layer_id} uninstalled. Your own files and settings were left as they were.")


def status(claude: Path) -> None:
    registry = load_registry(claude)
    if not registry["layers"]:
        print("no layers installed")
    for lid, layer in registry["layers"].items():
        print(f"{lid} {layer['version']} ({len(layer['files'])} files, installed {layer['installed']})")


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("command", choices=["install", "uninstall", "status"])
    parser.add_argument("target", nargs="?", help="layer source dir (install) or layer id (uninstall)")
    parser.add_argument("--claude-dir", default=str(Path.home() / ".claude"))
    args = parser.parse_args(argv)
    claude = Path(args.claude_dir)
    if args.command == "install":
        install(Path(args.target or "."), claude)
    elif args.command == "uninstall":
        if not args.target:
            parser.error("uninstall needs the layer id")
        uninstall(args.target, claude)
    else:
        status(claude)


if __name__ == "__main__":
    main()
