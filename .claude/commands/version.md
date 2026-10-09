---
name: version
description: "Show which version of proven-dotnet (and any layer on top of it) is installed, with a quick inventory of commands, agents and skills."
allowed-tools: Read, Bash
---

## Installed version

Report what is installed. Keep the answer short.

The installer records every layer in `~/.claude/.layers/registry.json`. Read it:

```bash
python3 -c "import json,os;r=json.load(open(os.path.expanduser('~/.claude/.layers/registry.json')));[print(k, v.get('version','?'), len(v.get('files',[])), 'files') for k,v in r.get('layers',{}).items()]" 2>/dev/null || echo "no registry"
```

(On Windows use `python` or `py -3` in place of `python3`.)

- One line per layer, e.g. **"proven 0.1.5"**, plus any team layer installed on top.
- No registry → proven-dotnet isn't installed with the installer (or it was copied by hand). Say so and
  suggest running `install.ps1` / `install.sh`.

Then a quick inventory, so the version has context. Count with the Glob tool (it works the same on
Windows): `~/.claude/commands/*.md`, `~/.claude/agents/*.md`, `~/.claude/skills/*/SKILL.md`.

These counts include anything the developer added themselves, so they can be higher than the layer's
own. Don't do anything else; this command only reads and reports.
