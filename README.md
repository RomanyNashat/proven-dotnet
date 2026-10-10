# proven-dotnet

Claude Code for .NET backends: every pattern proven in CI against real PostgreSQL, SQL Server, MongoDB,
Kafka, Redis, RabbitMQ and NATS.

Rules, skills, agents, commands and hooks that make Claude Code work like a senior .NET backend developer.
The code in the skills isn't just written: it compiles and runs in CI against real dependencies, and the
tests have caught real mistakes in the advice itself.

## Why "proven"

Each tested skill has three kinds of test:
- **Samples:** the code the skill shows, compiled and run against the real database or broker.
- **Stories:** a real situation as Given / When / Then. "The service was down when the report was due; it
  comes back; the report runs once."
- **Production tests:** the same code under a slim container image's conditions: no ICU, no time-zone
  database, UTC.

What they found in advice that looked right:
- `Microsoft.Data.SqlClient` refuses to connect without ICU, so a SQL Server service on a slim Alpine image
  can't reach its database at all.
- OpenIddict 6 on EF Core 10 throws on logout, and only on logout.
- In Production, ASP.NET Core doesn't check service lifetimes: a singleton holding a `DbContext` shares
  it across requests.
- Quartz silently drops a run that was due while the service was down.

The full list is in [CHANGELOG.md](CHANGELOG.md).

## Install (about two minutes)

You need [Claude Code](https://docs.claude.com/en/docs/claude-code) and Python 3 (the installer and the
hooks use it). The Roslyn server also needs the .NET 10 SDK.

```bash
git clone https://github.com/RomanyNashat/proven-dotnet.git
cd proven-dotnet
./install.sh                     # Windows: powershell -ExecutionPolicy Bypass -File install.ps1
./install-roslyn.sh              # optional: symbol search for Claude. Windows: install-roslyn.ps1
```

Restart Claude Code and run `/version`: it lists what's installed.

- **Update:** `git pull`, then run the installer again.
- **Remove:** `./uninstall.sh` (or `uninstall.ps1`). It takes out exactly what it put in.

## How it installs

Into your `~/.claude`, next to what you already have, and it never touches your own files:

- Its always-on rules go in `~/.claude/rules/proven/`. Your `CLAUDE.md` is left alone.
- An update removes only files it installed before and no longer ships. A skill or setting of yours with
  the same name is kept, and you get a warning.
- `settings.json` is merged key by key. A value you've set, or changed later, stays yours.
- A team can install its own layer on top (its rules, its choices, its skills) with the same installer.
  Where a team decides differently from the defaults here, the rules say "unless your team's rules say
  otherwise", and the team's layer says what it chose.

The installer is `tools/layer.py`, and its tests run on Linux and Windows on every change.

Skills for technologies only some services use (Cassandra, FHIR, BigQuery, Firebase, SignalR, NATS,
RabbitMQ, CAP, GitHub Actions) aren't installed. They're in [`project-skills/`](project-skills/), to copy
into the repos that need them; their README says which are tested.

## Status

**Before v1.0.0.** It's in daily use, but commands and skill names can still change. What's left:

- [x] Installer for Linux and Windows, tested on every change
- [x] The code in the skills compiled and run in CI against real dependencies
- [x] Story and production tests for the reference and per-project skills
- [ ] Story and production tests for the remaining installed skills (data, messaging, security, API,
      design and testing groups)
- [ ] Evals run for the generative commands (`/full-review`, `/health-check`, `/document`, `/stories`,
      `/patterns`) with and without proven-dotnet, and the results published
- [ ] A full run-through on real projects
- [ ] The Roslyn server measured over a few weeks of real use

## License

[MIT](LICENSE)
