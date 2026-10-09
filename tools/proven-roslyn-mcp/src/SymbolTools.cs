using System.ComponentModel;
using ProvenRoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace ProvenRoslynMcp.Tools;

/// <summary>
/// Semantic symbol navigation. Each tool returns compact JSON. Results are capped to keep
/// responses token-cheap; a "truncated" flag indicates when more results exist.
/// </summary>
[McpServerToolType]
public static class SymbolTools
{
    private const int MaxResults = 200;

    [McpServerTool(Name = "find_symbol")]
    [Description("Find declarations of a symbol by name (type, method, property, field) across the solution. Returns kind, full name, accessibility, and file:line. Use instead of grepping for a definition.")]
    public static Task<string> FindSymbol(
        SolutionManager solutionManager,
        [Description("Symbol name. Simple ('OrderService') or qualified ('Orders.OrderService').")] string name,
        CancellationToken ct)
        => Common.Guarded("find_symbol", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = await Common.ResolveAsync(solution, name, ct);

        var results = symbols.Take(MaxResults).Select(s => new
        {
            name = s.ToDisplayString(),
            kind = Common.KindOf(s),
            accessibility = s.DeclaredAccessibility.ToString().ToLowerInvariant(),
            containingType = s.ContainingType?.ToDisplayString(),
            @namespace = s.ContainingNamespace?.ToDisplayString(),
            location = Common.Loc(s),
        });

        return Common.Json(new
        {
            query = name,
            count = symbols.Count,
            truncated = symbols.Count > MaxResults,
            symbols = results,
        });
    });

    [McpServerTool(Name = "find_references")]
    [Description("Find all references to a symbol across the solution. Returns each reference as file:line. Use instead of grepping for usages.")]
    public static Task<string> FindReferences(
        SolutionManager solutionManager,
        [Description("Symbol name to find references to.")] string name,
        CancellationToken ct)
        => Common.Guarded("find_references", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = await Common.ResolveAsync(solution, name, ct);

        if (symbols.Count == 0)
        {
            return Common.Json(new { query = name, count = 0, references = Array.Empty<object>() });
        }

        var references = new List<object>();
        foreach (var symbol in symbols)
        {
            foreach (var referenced in await SymbolFinder.FindReferencesAsync(symbol, solution, ct))
            {
                foreach (var location in referenced.Locations)
                {
                    references.Add(new
                    {
                        symbol = symbol.ToDisplayString(),
                        location = Common.FromLocation(location.Location),
                    });
                }
            }
        }

        return Common.Json(new
        {
            query = name,
            count = references.Count,
            truncated = references.Count > MaxResults,
            references = references.Take(MaxResults),
        });
    });

    [McpServerTool(Name = "find_callers")]
    [Description("Find all callers of a method. Returns each calling member and the call site file:line. Use to trace who invokes a method.")]
    public static Task<string> FindCallers(
        SolutionManager solutionManager,
        [Description("Method name to find callers of.")] string methodName,
        CancellationToken ct)
        => Common.Guarded("find_callers", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = (await Common.ResolveAsync(solution, methodName, ct))
            .OfType<IMethodSymbol>()
            .ToList();

        var callers = new List<object>();
        foreach (var method in symbols)
        {
            foreach (var caller in await SymbolFinder.FindCallersAsync(method, solution, ct))
            {
                foreach (var location in caller.Locations)
                {
                    callers.Add(new
                    {
                        called = method.ToDisplayString(),
                        caller = caller.CallingSymbol.ToDisplayString(),
                        isDirect = caller.IsDirect,
                        location = Common.FromLocation(location),
                    });
                }
            }
        }

        return Common.Json(new
        {
            query = methodName,
            count = callers.Count,
            truncated = callers.Count > MaxResults,
            callers = callers.Take(MaxResults),
        });
    });

    [McpServerTool(Name = "find_implementations")]
    [Description("Find implementations of an interface or abstract member, or types deriving from a base. Returns implementing types/members with file:line.")]
    public static Task<string> FindImplementations(
        SolutionManager solutionManager,
        [Description("Interface, abstract type, or member name.")] string name,
        CancellationToken ct)
        => Common.Guarded("find_implementations", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = await Common.ResolveAsync(solution, name, ct);

        var implementations = new List<object>();
        foreach (var symbol in symbols)
        {
            IEnumerable<ISymbol> found = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct);
            foreach (var impl in found)
            {
                implementations.Add(new
                {
                    of = symbol.ToDisplayString(),
                    implementation = impl.ToDisplayString(),
                    kind = Common.KindOf(impl),
                    location = Common.Loc(impl),
                });
            }

            // If it's a class, also include derived classes.
            if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Class } namedType)
            {
                foreach (var derived in await SymbolFinder.FindDerivedClassesAsync(namedType, solution, transitive: false, cancellationToken: ct))
                {
                    implementations.Add(new
                    {
                        of = symbol.ToDisplayString(),
                        implementation = derived.ToDisplayString(),
                        kind = "derived-class",
                        location = Common.Loc(derived),
                    });
                }
            }
        }

        return Common.Json(new
        {
            query = name,
            count = implementations.Count,
            truncated = implementations.Count > MaxResults,
            implementations = implementations.Take(MaxResults),
        });
    });

    [McpServerTool(Name = "get_type_hierarchy")]
    [Description("Get the inheritance hierarchy of a type: base-class chain, implemented interfaces, and known derived types. Returns names with file:line.")]
    public static Task<string> GetTypeHierarchy(
        SolutionManager solutionManager,
        [Description("Type name to inspect.")] string typeName,
        CancellationToken ct)
        => Common.Guarded("get_type_hierarchy", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var type = (await Common.ResolveAsync(solution, typeName, ct))
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();

        if (type is null)
        {
            return Common.Json(new { query = typeName, found = false });
        }

        var baseChain = new List<object>();
        for (var b = type.BaseType; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
        {
            baseChain.Add(new { name = b.ToDisplayString(), location = Common.Loc(b) });
        }

        var interfaces = type.AllInterfaces
            .Select(i => new { name = i.ToDisplayString(), location = Common.Loc(i) })
            .ToList();

        var derived = (await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: false, cancellationToken: ct))
            .Select(d => new { name = d.ToDisplayString(), location = Common.Loc(d) })
            .Take(MaxResults)
            .ToList();

        return Common.Json(new
        {
            query = typeName,
            found = true,
            type = type.ToDisplayString(),
            kind = Common.KindOf(type),
            location = Common.Loc(type),
            baseChain,
            interfaces,
            derived,
        });
    });
}
