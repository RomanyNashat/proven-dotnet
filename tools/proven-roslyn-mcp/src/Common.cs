using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace ProvenRoslynMcp;

/// <summary>Shared helpers for compact JSON output and symbol resolution.</summary>
internal static class Common
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serialize a result object to a compact JSON string (the tool return value).</summary>

    /// <summary>
    /// Layered responses: return a compact first page and say how much was held back,
    /// rather than dumping every hit into the context window. A "find_references" on a
    /// common type can return hundreds of locations, most of which the caller never
    /// needed — and a huge result is its own reason to stop using a tool.
    /// Callers can ask for more with the tool's own limit parameter.
    /// </summary>
    public static (List<T> Shown, int Total, string? More) Page<T>(IEnumerable<T> items, int limit)
    {
        var all = items.ToList();
        if (limit <= 0) limit = DefaultPageSize;
        var shown = all.Take(limit).ToList();
        var more = all.Count > shown.Count
            ? $"{all.Count - shown.Count} more not shown (total {all.Count}). Re-run with a higher limit if you need them."
            : null;
        return (shown, all.Count, more);
    }

    /// <summary>Default page size for symbol results.</summary>
    public const int DefaultPageSize = 40;

    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>
    /// Every tool body runs through here. Two guarantees:
    /// 1. It RETURNS within the tool budget (PROVEN_TOOL_TIMEOUT_SECONDS, default 240) — cooperative work is
    ///    cancelled, and a hard WaitAsync backstop covers anything that ignores cancellation. A silent
    ///    tool call is what Claude Code waits 30 minutes on before giving up.
    /// 2. Failures come back as a readable JSON result (what went wrong + what to do instead), never as a
    ///    thrown exception — the MCP SDK may reduce an exception to a generic "An error occurred", which
    ///    loses the one line that says why.
    /// A cancellation by the CLIENT is still honoured as a cancellation.
    /// </summary>
    public static async Task<string> Guarded(string tool, CancellationToken ct, Func<CancellationToken, Task<string>> body)
    {
        var raw = Environment.GetEnvironmentVariable("PROVEN_TOOL_TIMEOUT_SECONDS");
        var budget = TimeSpan.FromSeconds(int.TryParse(raw, out var s) && s > 0 ? s : 240);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        try
        {
            return await body(cts.Token).WaitAsync(budget + TimeSpan.FromSeconds(10), ct);
        }
        catch (Workspace.WorkspaceNotReadyException ex)
        {
            return Json(new { tool, status = "loading", message = ex.Message });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is OperationCanceledException or TimeoutException)
        {
            Workspace.Diag.Write($"{tool}: exceeded {budget.TotalSeconds:0}s budget");
            return Json(new
            {
                tool,
                status = "timeout",
                message = $"{tool} did not finish within {budget.TotalSeconds:0}s. Answer this one with text search, " +
                          "or narrow the query (a qualified name instead of a common simple name).",
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Workspace.Diag.Write($"{tool}: {ex.GetType().Name}: {ex.Message}");
            return Json(new { tool, status = "error", message = ex.Message });
        }
    }

    /// <summary>The first in-source declaration location of a symbol, as { file, line }.</summary>
    public static object? Loc(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location is null ? null : LineSpan(location.GetLineSpan());
    }

    /// <summary>A Roslyn location's file + 1-based line, as { file, line }.</summary>
    public static object LineSpan(FileLinePositionSpan span) => new
    {
        file = span.Path,
        line = span.StartLinePosition.Line + 1,
    };

    public static object FromLocation(Location location) => LineSpan(location.GetLineSpan());

    /// <summary>True if the symbol is declared in source within the solution.</summary>
    public static bool InSource(this ISymbol symbol) => symbol.Locations.Any(l => l.IsInSource);

    /// <summary>
    /// Resolve a name to matching source-declared symbols across the solution.
    /// Accepts a simple name ("OrderService") or a qualified name ("Orders.OrderService");
    /// a qualified name filters by display-string suffix.
    /// </summary>
    public static async Task<List<ISymbol>> ResolveAsync(
        Solution solution, string name, CancellationToken ct)
    {
        var simpleName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

        var declarations = await SymbolFinder.FindSourceDeclarationsAsync(
            solution, simpleName, ignoreCase: false, ct);

        var matches = declarations.ToList();

        if (name.Contains('.'))
        {
            matches = matches
                .Where(s =>
                {
                    var display = s.ToDisplayString();
                    return display == name || display.EndsWith("." + name, StringComparison.Ordinal);
                })
                .ToList();
        }

        return matches;
    }

    /// <summary>All named types under a namespace (including nested types), recursively.</summary>
    public static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in AllTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in NestedTypes(nested))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Short, readable description of a symbol's kind for output.</summary>
    public static string KindOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        INamespaceSymbol => "namespace",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };
}
