# proven-dotnet Roslyn MCP Server

A small **Model Context Protocol (MCP) server** that gives Claude Code semantic, token-cheap
navigation of your .NET solution using Roslyn — instead of reading whole files into context.

This is the one **compiled** component of proven-dotnet. Everything else is markdown; this is a real
C# program you build once and install. After that it runs in the background and Claude uses it
automatically.

## Why it exists

When Claude needs to understand your code, it normally reads files — often 500–2000 tokens to
answer "where is this method called?". This server answers the same question from Roslyn's
semantic model in ~30–150 tokens. On a large solution that is roughly a 10× reduction in tokens
spent on code exploration, which means faster, cheaper sessions — directly helping the startup/
context cost you've been fighting.

## Tools (12)

| Tool | What it answers |
|------|-----------------|
| `find_symbol` | Where is X declared? (type/method/property/field) |
| `find_references` | Everywhere X is used |
| `find_callers` | Who calls this method? |
| `find_implementations` | What implements this interface / derives from this base? |
| `get_type_hierarchy` | Base chain, interfaces, derived types of a type |
| `get_public_api` | The public surface of a service/namespace |
| `get_diagnostics` | Errors/warnings from a real compile, with file:line |
| `find_dead_code` | Private/internal members with no references (heuristic) |
| `detect_circular_dependencies` | Namespace cycles (signature-level) |
| `get_test_coverage_map` | Which production types are referenced by test code (structural) |
| `rename_symbol` | Rename a symbol across the solution (preview diff, or apply atomically with rollback) |
| `find_tests_for_symbol` | Which tests exercise a production symbol (references in test projects) |
| `workspace_status` | Instant: loading / ready / failed, the current stage, the last project touched |

> **`rename_symbol` and `find_tests_for_symbol`:** `rename_symbol` uses Roslyn's `Renamer`
> so every reference updates correctly (unlike text find/replace). It defaults to **preview** (compute
> the diff, write nothing); pass `preview=false` to apply, and it writes **atomically with rollback** —
> if any file write fails, all already-written files are restored. Public symbols carry a
> "coordinate/version this" warning since cross-service consumers aren't visible to Roslyn.
> `find_tests_for_symbol` finds references that live in test projects — the parity gate uses it to know
> what already covers a symbol before it changes. These power the plain-naming atom and the parity gate.

## Prerequisites

- .NET SDK installed (8.0 or newer; .NET 10 is fine — the tool targets net8.0 and rolls forward)
- The machine where you run it can see your solution's source

## Build & Install

From this folder (`tools/proven-roslyn-mcp/`):

```bash
# 1. Restore + build
dotnet build -c Release

# 2. Pack and install as a global tool
dotnet pack -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg Proven.Roslyn.Mcp

# (to update later)
dotnet tool update --global --add-source ./nupkg Proven.Roslyn.Mcp
```

Or just let the install script do it (from the package root):
`./install-roslyn.ps1` (Windows) or `./install-roslyn.sh` (bash). On a company Windows laptop that
blocks unsigned scripts, run it as
`powershell -ExecutionPolicy Bypass -File .\install-roslyn.ps1`.

After install, `proven-roslyn-mcp` is on your PATH.

> **First build may need version tweaks.** The package versions in `ProvenRoslynMcp.csproj` are
> known-good starting points, but the MCP SDK is in preview and moves. If restore complains about
> a version, run `dotnet add package ModelContextProtocol` (and, if needed,
> `dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces`) to pull the versions your feed
> offers, then rebuild. This back-and-forth is expected for the compiled component.

## Register with Claude Code

There are two ways to register the server. Both work; the difference is **where the config lives
and whether anything lands in your repo.**

### Option A — User scope (recommended: zero footprint in any repo)

Registers the server once, globally, available in every project — and **nothing is ever written
into a repo**. The config lives in your home directory (`~/.claude.json` /
`%USERPROFILE%\.claude.json`), private to you.

```bash
claude mcp add --scope user --transport stdio proven-roslyn proven-roslyn-mcp
```

If your repo has more than one `.sln`, also set the solution path so auto-detect isn't ambiguous:

```bash
claude mcp add --scope user --transport stdio proven-roslyn proven-roslyn-mcp --env PROVEN_SOLUTION_PATH=C:\Projects\Shop\Shop.sln
```

This is the best fit if you don't want to touch the repos at all.

### Option B — Project scope (a `.mcp.json` in the repo)

Copy `.mcp.json.example` to the **project root** as `.mcp.json` (or merge the `mcpServers` block
into an existing one):

```json
{
  "mcpServers": {
    "proven-roslyn": {
      "command": "proven-roslyn-mcp",
      "args": [],
      "env": { "PROVEN_SOLUTION_PATH": "" }
    }
  }
}
```

By default a project-scope `.mcp.json` is meant to be committed and shared with the team. **If you
want it local-only, add it to your `.gitignore`** so it never gets committed:

```
# .gitignore — keep the local Roslyn MCP config out of git
.mcp.json
```

- Leave `PROVEN_SOLUTION_PATH` empty to auto-detect a single `.sln`, or set the full path if the repo
  has more than one.
- Restart Claude Code. It launches the server over stdio and the ten tools become available.

### Which to use, and how they interact

- **Don't want anything in the repos →** use **Option A** (user scope). Done.
- **Want a specific repo to have its own settings →** use **Option B** and gitignore it.
- **Both at once?** Claude Code resolves by precedence — **local > project > user** — and uses a
  single definition from the highest-precedence source (entries are not merged across scopes). So
  you can register globally with Option A *and* drop a project `.mcp.json` in one repo that needs
  different settings; the project file wins for that repo, the global applies everywhere else.
  That gives you exactly "use the repo's config if present, otherwise fall back to the global one."

> Known quirk: in some Claude Code versions, `--scope user` servers are stored keyed to a project
> path rather than truly machine-wide, so they may not appear in a brand-new project. If that
> happens, re-run the `claude mcp add --scope user` command from that project, or use Option B with
> a gitignored `.mcp.json`.

Verify it's connected by asking Claude something like *"use find_symbol to locate
NotificationConsumer"* — it should return a file:line without reading files.

## Startup, loading & health

- **Instant handshake.** The MCP handshake completes immediately. MSBuild registration and the solution
  load run in the background right after it, so the workspace is usually warm by the first question.
- **No call ever hangs.** A tool call waits at most `PROVEN_READY_WAIT_SECONDS` (45) for a load in
  progress, then answers `"status":"loading"` so Claude uses text search for that question. Every tool
  call returns within `PROVEN_TOOL_TIMEOUT_SECONDS` (240) with a result, `"timeout"` or `"error"`, never
  a silence Claude Code has to wait out.
- **Hard load limit.** A load that has not finished after `PROVEN_SOLUTION_LOAD_TIMEOUT_SECONDS` (120)
  is abandoned, even if the stuck step ignores cancellation. The error names the stage and the last
  project touched. A failed load is remembered for `PROVEN_RETRY_AFTER_SECONDS` (60) so repeated calls
  fail instantly; after that the next call tries a fresh load.
- **Health / version.** `proven-roslyn-mcp --version` prints the version and exits without loading anything.

### Why there is exactly one load task

An earlier version's background warm-up awaited itself: the warm-up task called the method that
joins "the warm-up in flight", which was the same task. The load never started, and every tool call
joined the dead task and waited until Claude Code's 30-minute idle limit for stdio servers. The load
timeout never fired because it was set further down the method that never ran. It reproduced on a
two-project solution. The rewrite has exactly one load task, and nothing in it can wait on itself.

## Diagnosing a slow or stuck load

Run this from the repo folder:
```
proven-roslyn-mcp --diagnose
```
It runs the same load the server does, in the foreground, and prints every stage and every project
(`Evaluate` → `Build` → `Resolve`) with timings. It ends with `OK - N project(s) loaded.` or with
`FAILED -` naming where it stopped. If one project is always the last line, its design-time build is
what hangs. Typical causes are a network share, a credential prompt, or a custom MSBuild target.

Every load is also written to **`~/.claude/proven-roslyn.log`** (override with `PROVEN_ROSLYN_LOG`;
truncated at 512 KB). It holds stages, project names and timings, never source code. When Roslyn
misbehaves in a session, this file shows why.

## How the solution is found

1. `PROVEN_SOLUTION_PATH` environment variable (if it points to an existing `.sln` or `.slnx`)
2. A single `.sln` (else `.slnx`) at the project root
3. A single `.sln` (else `.slnx`) found recursively, skipping `bin/`, `obj/`, `.git/`, `node_modules/`
   and folders it can't read

If multiple solutions are found and none is specified, the server asks you (via an error) to set
`PROVEN_SOLUTION_PATH`.

## Notes & limits

- **stdio transport:** the server speaks MCP on stdout, so all its logging goes to stderr. If you
  run it directly in a terminal you'll see load logs on stderr — that's normal.
- **Heuristic tools** (`find_dead_code`, `detect_circular_dependencies`, `get_test_coverage_map`)
  surface candidates, not certainties — each result is labeled with its caveat. Dead-code results
  can include members used via reflection/DI/serialization; circular-dependency detection is
  signature-level (not method-body); the coverage map is structural (use `/coverage` for real
  line/branch percentages).
- **Read-only except one tool:** `rename_symbol` with `preview=false` writes files (atomically, with
  rollback). Every other tool only reads and analyzes.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `restore` fails on a package version | `dotnet add package ModelContextProtocol` to get the latest preview; rebuild |
| "No .sln found" | Set `PROVEN_SOLUTION_PATH` in `.mcp.json` |
| MSBuild/Workspace load warnings on stderr | Usually harmless (missing optional targets); tools still work |
| Tool runs but returns empty | Confirm the right `.sln` is loaded; check the path in `PROVEN_SOLUTION_PATH` |
| Server not found by Claude Code | Ensure `proven-roslyn-mcp` is on PATH (`dotnet tool list -g`) and `.mcp.json` is in the project root |
| `No file format header found … .slnx` | An old build on Roslyn 4.14, which can't read `.slnx`. This server uses Roslyn 5.0.0: rebuild it with `install-roslyn` |
| `Found 2 *.sln files under …` (lists them) | Claude Code was started in a folder above several repos. Start it inside the repo, or add a `.mcp.json` in that folder only (see below). Never set `PROVEN_SOLUTION_PATH` on the global registration: it would apply to every repo |
| Calls answer `"status":"loading"` for a long time | Run `proven-roslyn-mcp --diagnose` in the repo folder; read `~/.claude/proven-roslyn.log` |
| `/mcp` shows **proven-roslyn · failed** with "connection timed out after 30000ms" | Almost always the **startup-window** problem below — not the server. |

## "proven-roslyn · failed" / connection timed out — the real cause and fix

If `/mcp` shows the server *failed* but `proven-roslyn-mcp --version` prints `1.1.0.0` and exits
instantly, **the server is fine** — the failure is Claude Code's own slow startup eating the MCP
connect window. In corporate networks, Claude Code's startup network calls (policy limits, telemetry,
`claudeai-mcp`) can be slow or blocked, and the MCP connect timer runs *during* that. The debug log
shows the server's `initialize` handler completing successfully, yet the connection is marked failed
because the **total** time crossed the 30s default.

**Fix — raise the MCP connect timeout.** proven-dotnet ships `MCP_TIMEOUT: 120000` in `settings.json` (env
block), which gives the connection 120s instead of 30s. If you're on this build, it's already set;
confirm with:
```
type %USERPROFILE%\.claude\settings.json   (Windows)   — look for "MCP_TIMEOUT": "120000" in "env"
```
(Note: some Claude Code versions cap `MCP_TIMEOUT` at 60000ms — if 120000 isn't honored, 60000 is
usually still enough, since the server itself responds in well under a second.)

**To recover a failed connection in-session — Claude Code does the reconnect, not a slash command:**
1. Run `/mcp`
2. Select **proven-roslyn**
3. Choose **reconnect** (or run `/mcp reconnect proven-roslyn` on versions that support it)

Reconnecting outside the cold-start window almost always succeeds, because it isn't competing with
Claude Code's startup fetches. There is no command for this on purpose: re-spawning an MCP server
is a Claude Code *runtime* action — a custom slash command can't perform it, only remind you to run
`/mcp`.

**The deeper fix (optional, IT-side):** the root slowness is Claude Code's startup fetches timing out
in the corporate network. Allow-listing Claude Code's Anthropic endpoints in the proxy/firewall fixes
the actual delay rather than papering over it with a longer timeout.
