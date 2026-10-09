# proven-dotnet

Claude Code for .NET backends: every pattern proven in CI against real PostgreSQL, SQL Server,
MongoDB, Kafka and Redis.

Rules, skills, agents, commands and hooks that make Claude Code work like a senior .NET backend
developer. The code in the skills isn't just written: it compiles and runs in CI against real
dependencies, and the tests have caught real mistakes in the advice itself.

## Status

**Being built.** The pieces are moving in from a harness that has been in daily use since March 2026:
the installer, the rules, the tested skills, the hooks and the Roslyn server are here; agents and commands come next. Until
v1.0.0 there is nothing to install yet. See [CHANGELOG.md](CHANGELOG.md).

## How it installs

Into your `~/.claude`, next to what you already have, and it never touches your own files:

- Its always-on rules go in `~/.claude/rules/proven/`. Your `CLAUDE.md` is left alone.
- An update removes only files it installed before and no longer ships. A skill or setting of yours
  with the same name is kept, and you get a warning.
- `settings.json` is merged key by key. A value you've set, or changed later, stays yours.
- `uninstall` takes out exactly what it put in.

The installer is `tools/layer.py`, and its tests run on Linux and Windows on every change.

## License

[MIT](LICENSE)
