using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace ProvenRoslynMcp.Workspace;

/// <summary>
/// Thrown when a tool needs the solution but the load is still running. Tools turn this into a fast
/// "still loading — use text search for this one" answer instead of blocking the caller.
/// </summary>
public sealed class WorkspaceNotReadyException(string message) : Exception(message);

/// <summary>
/// Loads a .NET solution once via MSBuildWorkspace and caches it for all tool calls.
/// The solution is resolved from PROVEN_SOLUTION_PATH, or a single .sln/.slnx under the working directory.
///
/// Why this class is built the way it is:
/// an earlier background warm-up ran GetSolutionAsync INSIDE the warm-up task, and GetSolutionAsync
/// joined "the warm-up in flight" — i.e. the warm-up awaited itself. Deadlock. The load never started,
/// the 120s load timeout (set further down that method) was never reached, and every tool call joined
/// the dead task and hung until Claude Code's 30-minute stdio idle limit. Only the rare run where the
/// thread-pool started the lambda before the field was assigned ever worked.
///
/// Rules now:
/// 1. ONE load task, started by <see cref="EnsureLoadStarted"/>. The load body never calls back into
///    GetSolutionAsync, so nothing can wait on itself.
/// 2. The load timeout is a HARD bound (Task.WaitAsync), not just a cancellation request — a design-time
///    build stuck in the out-of-proc BuildHost ignores cancellation, and CancelAfter alone never returns.
/// 3. A tool call waits at most PROVEN_READY_WAIT_SECONDS for a load in progress, then gets a fast
///    "still loading" answer. The load keeps going in the background.
/// 4. Every stage is recorded (and written to the log file), so a stall says WHERE it stalled.
/// 5. A failed load is remembered for a short cooldown, so repeated calls fail instantly with the real
///    reason instead of each re-running a slow load.
/// </summary>
public sealed class SolutionManager
{
    private readonly object _sync = new();
    private readonly ILogger<SolutionManager> _log;

    private Task<Solution>? _load;
    private volatile Solution? _solution;

    // Observable state for workspace_status and for error messages.
    private volatile string _stage = "not started";
    private DateTime? _loadStartedUtc;
    private DateTime? _failedAtUtc;
    private string? _failure;
    private string? _solutionPath;
    private volatile string? _lastProject;
    private int _projectsLoaded;

    private static bool _msbuildRegistered;

    public SolutionManager(ILogger<SolutionManager> log) => _log = log;

    /// <summary>Start loading in the background, right after the MCP handshake. Never blocks.</summary>
    public void StartWarmUp()
    {
        var load = EnsureLoadStarted();
        // Observe the exception so a failed warm-up is never an unobserved-task crash;
        // the failure is recorded and reported on the next tool call / status check.
        load.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// The loaded solution. Waits up to PROVEN_READY_WAIT_SECONDS for a load in progress; after that throws
    /// <see cref="WorkspaceNotReadyException"/> so the caller can answer from text search instead.
    /// </summary>
    public async Task<Solution> GetSolutionAsync(CancellationToken ct)
    {
        var ready = _solution;
        if (ready is not null)
        {
            return ready;
        }

        var load = EnsureLoadStarted();
        try
        {
            return await load.WaitAsync(ResolveSeconds("PROVEN_READY_WAIT_SECONDS", 45), ct);
        }
        catch (TimeoutException)
        {
            throw new WorkspaceNotReadyException(
                $"The solution is still loading ({Describe()}). Answer this question with text search; " +
                "the load continues in the background — call workspace_status, or retry in a minute.");
        }
    }

    /// <summary>Gets compilations for every project in the solution.</summary>
    public async Task<IReadOnlyList<(Project Project, Compilation Compilation)>> GetCompilationsAsync(
        CancellationToken ct)
    {
        var solution = await GetSolutionAsync(ct);
        var result = new List<(Project, Compilation)>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is not null)
            {
                result.Add((project, compilation));
            }
        }

        return result;
    }

    /// <summary>Instant snapshot for workspace_status. Never waits and never starts a load.</summary>
    public object Status()
    {
        var load = _load;
        var state = _solution is not null ? "ready"
            : load is null ? "not_started"
            : load.IsFaulted ? "failed"
            : "loading";

        return new
        {
            state,
            stage = _stage,
            solution = _solutionPath,
            secondsSinceLoadStarted = _loadStartedUtc is { } s ? Math.Round((DateTime.UtcNow - s).TotalSeconds, 1) : (double?)null,
            projectsLoaded = _solution?.Projects.Count() ?? Volatile.Read(ref _projectsLoaded),
            lastProject = _lastProject,
            error = state == "failed" ? _failure : null,
            log = Diag.LogPath,
        };
    }

    // ── the one load ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the load in flight, the completed load, or a recent failure (cooldown); otherwise
    /// starts a fresh load. The only place a load is ever started.
    /// </summary>
    private Task<Solution> EnsureLoadStarted()
    {
        lock (_sync)
        {
            if (_load is not null)
            {
                var inCooldown = _load.IsFaulted && _failedAtUtc is { } f &&
                                 DateTime.UtcNow - f < ResolveSeconds("PROVEN_RETRY_AFTER_SECONDS", 60);
                if (!_load.IsFaulted || inCooldown)
                {
                    return _load;
                }
            }

            _failure = null;
            _failedAtUtc = null;
            _loadStartedUtc = DateTime.UtcNow;
            Volatile.Write(ref _projectsLoaded, 0);
            _lastProject = null;
            _load = Task.Run(LoadAsync);
            return _load;
        }
    }

    private async Task<Solution> LoadAsync()
    {
        var clock = Stopwatch.StartNew();
        MSBuildWorkspace? workspace = null;
        try
        {
            Stage("locating solution");
            var path = ResolveSolutionPath();
            _solutionPath = path;

            Stage("registering MSBuild");
            EnsureMSBuildRegistered();

            Stage("opening solution");
            workspace = CreateWorkspace();
            var timeout = ResolveSeconds("PROVEN_SOLUTION_LOAD_TIMEOUT_SECONDS", 120);
            using var cancel = new CancellationTokenSource(timeout);
            var open = workspace.OpenSolutionAsync(path, progress: new LoadProgress(this), cancellationToken: cancel.Token);

            Solution solution;
            try
            {
                // HARD bound: returns at the deadline even if the load ignores the cancellation request.
                solution = await open.WaitAsync(timeout + TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // A stuck BuildHost can hold files open; dispose off-thread in case Dispose blocks too.
                var stuck = workspace;
                workspace = null;
                _ = Task.Run(() => { try { stuck.Dispose(); } catch { /* best effort */ } });
                throw new InvalidOperationException(
                    $"Solution load did not finish within {timeout.TotalSeconds:0}s — stuck at: {Describe()}. " +
                    "If one project is always last, its design-time build is what hangs (a network share, a " +
                    "credential prompt, a custom target). Run `proven-roslyn-mcp --diagnose` in this folder to watch " +
                    "it live, set PROVEN_SOLUTION_PATH to a smaller .sln, or raise PROVEN_SOLUTION_LOAD_TIMEOUT_SECONDS.");
            }

            _solution = solution;
            workspace = null; // keep it alive with the solution
            Stage($"ready — {solution.Projects.Count()} project(s) in {clock.Elapsed.TotalSeconds:0.0}s");
            return solution;
        }
        catch (Exception ex)
        {
            workspace?.Dispose();
            _failure = ex.Message;
            _failedAtUtc = DateTime.UtcNow;
            Stage($"failed after {clock.Elapsed.TotalSeconds:0.0}s");
            _log.LogError("Solution load failed: {Message}", ex.Message);
            Diag.Write($"ERROR {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Separate, non-inlined method so no MSBuild-dependent type is JIT-compiled before
    /// MSBuildLocator has registered — the classic MSBuildLocator trap.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private MSBuildWorkspace CreateWorkspace()
    {
        var workspace = MSBuildWorkspace.Create();
        // Load diagnostics are warnings, not failures — log and continue.
        workspace.WorkspaceFailed += (_, e) =>
        {
            _log.LogWarning("Workspace load: {Message}", e.Diagnostic.Message);
            Diag.Write($"warning: {e.Diagnostic.Message}");
        };
        return workspace;
    }

    /// <summary>Registers MSBuild once, on first load (off the startup/handshake path).</summary>
    private void EnsureMSBuildRegistered()
    {
        if (_msbuildRegistered)
        {
            return;
        }

        if (!MSBuildLocator.IsRegistered)
        {
            var instance = MSBuildLocator.RegisterDefaults();
            _log.LogInformation("MSBuild registered: {Path}", instance.MSBuildPath);
            Diag.Write($"MSBuild: {instance.MSBuildPath} ({instance.Version})");
        }

        _msbuildRegistered = true;
    }

    private void Stage(string stage)
    {
        _stage = stage;
        _log.LogInformation("Workspace: {Stage}", stage);
        Diag.Write(stage + (_solutionPath is null ? "" : $"  [{_solutionPath}]"));
    }

    private string Describe()
    {
        var elapsed = _loadStartedUtc is { } s ? $"{(DateTime.UtcNow - s).TotalSeconds:0}s" : "?";
        var last = _lastProject is null ? "" : $", last: {_lastProject}";
        return $"{_stage}, {Volatile.Read(ref _projectsLoaded)} project(s) so far, {elapsed} elapsed{last}";
    }

    /// <summary>Per-project progress from MSBuildWorkspace — what makes a stall locatable.</summary>
    private sealed class LoadProgress(SolutionManager owner) : IProgress<ProjectLoadProgress>
    {
        public void Report(ProjectLoadProgress value)
        {
            var tfm = string.IsNullOrEmpty(value.TargetFramework) ? "" : $" ({value.TargetFramework})";
            owner._lastProject = $"{Path.GetFileName(value.FilePath)} {value.Operation}{tfm}";
            if (value.Operation == ProjectLoadOperation.Resolve)
            {
                Interlocked.Increment(ref owner._projectsLoaded);
            }

            Diag.Write($"  {value.Operation,-8} {value.ElapsedTime.TotalSeconds,6:0.0}s  {Path.GetFileName(value.FilePath)}{tfm}");
        }
    }

    // ── configuration ───────────────────────────────────────────────────────────────────────────

    private static TimeSpan ResolveSeconds(string variable, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        return TimeSpan.FromSeconds(int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : fallback);
    }

    /// <summary>
    /// Resolves which solution to analyze:
    /// 1. PROVEN_SOLUTION_PATH env var (if it points to an existing file)
    /// 2. A single .sln (else .slnx) at the working-directory root
    /// 3. A single .sln (else .slnx) found recursively, skipping bin/obj/.git/node_modules and
    ///    folders it may not read (junctions and locked folders on Windows used to throw here)
    /// Otherwise throws with guidance to set PROVEN_SOLUTION_PATH.
    /// </summary>
    private const string ChooseHint =
        "Start Claude Code inside the repo folder you mean, or pick one for this folder only with a .mcp.json " +
        "here that sets PROVEN_SOLUTION_PATH. Never set it on the global (user-scope) registration: that would " +
        "point every repo at this one solution.";

    private static string ResolveSolutionPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("PROVEN_SOLUTION_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var cwd = Directory.GetCurrentDirectory();
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            var atRoot = Directory.GetFiles(cwd, pattern, SearchOption.TopDirectoryOnly);
            if (atRoot.Length == 1)
            {
                return atRoot[0];
            }

            if (atRoot.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Found {atRoot.Length} {pattern} files in {cwd}: {string.Join(", ", atRoot.Take(5))}. " + ChooseHint);
            }
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        static bool Skipped(string p)
        {
            var sep = Path.DirectorySeparatorChar;
            return new[] { "bin", "obj", ".git", "node_modules" }.Any(d => p.Contains($"{sep}{d}{sep}"));
        }

        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            var found = Directory.EnumerateFiles(cwd, pattern, options).Where(p => !Skipped(p)).Take(20).ToList();
            if (found.Count == 1)
            {
                return found[0];
            }

            if (found.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Found {(found.Count == 20 ? "20+" : found.Count.ToString())} {pattern} files under {cwd}: " +
                    $"{string.Join(", ", found.Take(5).Select(p => Path.GetRelativePath(cwd, p)))}. " + ChooseHint);
            }
        }

        throw new InvalidOperationException(
            "No .sln or .slnx found under the working directory. Set PROVEN_SOLUTION_PATH to your solution file.");
    }
}

/// <summary>
/// Small append-only diagnostics log (default ~/.claude/proven-roslyn.log, override with PROVEN_ROSLYN_LOG).
/// Stderr goes to Claude Code and is hard to find afterwards; this file is where a stall can be read.
/// Bounded: truncated when it passes 512 KB. Holds paths, project names and timings — no source code.
/// </summary>
internal static class Diag
{
    private static readonly object Gate = new();

    /// <summary>--diagnose mode: also print each line to the console as it happens.</summary>
    public static bool Echo { get; set; }

    public static string LogPath { get; } =
        Environment.GetEnvironmentVariable("PROVEN_ROSLYN_LOG") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "proven-roslyn.log");

    public static void Write(string line)
    {
        if (Echo)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
        }

        try
        {
            lock (Gate)
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    File.WriteAllText(LogPath, "(log truncated at 512 KB)" + Environment.NewLine);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break the server.
        }
    }
}
