using ProvenRoslynMcp;
using ProvenRoslynMcp.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// proven-dotnet Roslyn MCP Server — entry point
//
// Gives Claude Code semantic navigation of a .NET solution (find symbol, references,
// callers, implementations, type hierarchy, public API, diagnostics, dead code,
// circular dependencies, test-coverage map) at a fraction of the tokens of reading files.
//
// Transport: stdio. IMPORTANT: stdout carries the MCP protocol, so ALL logging must go
// to stderr (configured below). Never Console.WriteLine from tools.
// ─────────────────────────────────────────────────────────────────────────────

// ── Health / version short-circuit ───────────────────────────────────────────
// Respond and EXIT without launching the server or touching MSBuild/Roslyn, so
// `proven-roslyn-mcp --version` (and diagnostics) return instantly instead of hanging.
if (args.Length > 0 &&
    (args[0] is "--version" or "-v" or "--health" or "version" or "health"))
{
    var asmVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    Console.WriteLine($"proven-roslyn-mcp {asmVersion}");
    Console.WriteLine("ok");
    return;
}

// ── Diagnose: load the solution in THIS folder in the foreground and show every stage ─────────
// The way to see where a load stalls: `proven-roslyn-mcp --diagnose` from the repo folder. Not MCP
// mode, so printing to the console is fine here. Uses the same loader and limits as the server.
if (args.Length > 0 && args[0] is "--diagnose" or "diagnose")
{
    Diag.Echo = true;
    Environment.SetEnvironmentVariable("PROVEN_READY_WAIT_SECONDS", "86400"); // wait for the load itself to finish or time out
    using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning).AddSimpleConsole(o => o.SingleLine = true));
    var manager = new SolutionManager(loggerFactory.CreateLogger<SolutionManager>());
    try
    {
        var solution = await manager.GetSolutionAsync(CancellationToken.None);
        Console.WriteLine($"OK - {solution.Projects.Count()} project(s) loaded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED - {ex.Message}");
        Environment.ExitCode = 1;
    }

    Console.WriteLine(Common.Json(manager.Status()));
    Console.WriteLine($"Log: {Diag.LogPath}");
    return;
}

// NOTE: MSBuild registration is intentionally DEFERRED. It used to run here, before
// the host started — and registering + resolving MSBuild assemblies is what delayed
// the stdio handshake past Claude Code's 30s timeout. We now register lazily, inside
// SolutionManager, on the first real tool call. The handshake completes immediately.

var builder = Host.CreateApplicationBuilder(args);

// Route every log to stderr so stdout stays clean for the MCP protocol.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// The loaded solution is shared across all tool calls.
builder.Services.AddSingleton<SolutionManager>();

// Register the MCP server with stdio transport and discover [McpServerTool] methods.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

Diag.Write($"server start {typeof(Program).Assembly.GetName().Version} in {Directory.GetCurrentDirectory()}");

// Handshake first, then warm the workspace in the background.
// The MCP initialize must return immediately — a blocking solution load is what caused
// the original 30s handshake timeout. But a purely LAZY load just moves the stall onto
// the first symbol question, which is exactly the moment a caller decides whether this
// server is worth using; a multi-second wait there sends them back to grep for good.
// So: build, start loading off-thread, then run.
host.Services.GetRequiredService<SolutionManager>().StartWarmUp();

await host.RunAsync();
