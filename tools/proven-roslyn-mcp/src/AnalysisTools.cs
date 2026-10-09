using System.ComponentModel;
using ProvenRoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace ProvenRoslynMcp.Tools;

/// <summary>
/// Higher-level analysis tools: public API surface, diagnostics, and three heuristic
/// analyses (dead code, circular dependencies, test-coverage map). The heuristic tools
/// are clearly labeled — they surface candidates, not certainties.
/// </summary>
[McpServerToolType]
public static class AnalysisTools
{
    private const int MaxResults = 200;
    private const int MaxDeadCodeCandidates = 500;

    [McpServerTool(Name = "get_public_api")]
    [Description("List the public API surface (public types and their public members) of the solution, optionally filtered to a namespace substring. Use to understand what a service exposes without reading files.")]
    public static Task<string> GetPublicApi(
        SolutionManager solutionManager,
        [Description("Optional namespace substring filter, e.g. 'NotificationService'. Empty = whole solution.")] string? namespaceFilter,
        CancellationToken ct)
        => Common.Guarded("get_public_api", ct, async ct =>
    {
        var compilations = await solutionManager.GetCompilationsAsync(ct);
        var types = new List<object>();
        var count = 0;

        foreach (var (_, compilation) in compilations)
        {
            foreach (var type in Common.AllTypes(compilation.GlobalNamespace))
            {
                if (!type.InSource() || type.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                var fullName = type.ToDisplayString();
                if (!string.IsNullOrWhiteSpace(namespaceFilter) &&
                    !fullName.Contains(namespaceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
                if (types.Count >= MaxResults)
                {
                    continue;
                }

                var members = type.GetMembers()
                    .Where(m => m.DeclaredAccessibility == Accessibility.Public
                             && !m.IsImplicitlyDeclared
                             && m is not INamedTypeSymbol)
                    .Select(m => new { name = m.Name, kind = Common.KindOf(m), signature = m.ToDisplayString() })
                    .ToList();

                types.Add(new
                {
                    name = fullName,
                    kind = Common.KindOf(type),
                    location = Common.Loc(type),
                    members,
                });
            }
        }

        return Common.Json(new
        {
            filter = namespaceFilter,
            count,
            truncated = count > MaxResults,
            types,
        });
    });

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Compile the solution and return diagnostics (errors and warnings) with id, severity, message, and file:line. Use to see build problems without running a full build.")]
    public static Task<string> GetDiagnostics(
        SolutionManager solutionManager,
        [Description("Minimum severity: 'error', 'warning' (default), or 'info'.")] string? minimumSeverity,
        CancellationToken ct)
        => Common.Guarded("get_diagnostics", ct, async ct =>
    {
        var threshold = (minimumSeverity?.ToLowerInvariant()) switch
        {
            "error" => DiagnosticSeverity.Error,
            "info" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Warning,
        };

        var compilations = await solutionManager.GetCompilationsAsync(ct);
        var diagnostics = new List<object>();
        var count = 0;

        foreach (var (project, compilation) in compilations)
        {
            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                if (diagnostic.Severity < threshold || diagnostic.Location.Kind != LocationKind.SourceFile)
                {
                    continue;
                }

                count++;
                if (diagnostics.Count >= MaxResults)
                {
                    continue;
                }

                diagnostics.Add(new
                {
                    project = project.Name,
                    id = diagnostic.Id,
                    severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                    message = diagnostic.GetMessage(),
                    location = Common.FromLocation(diagnostic.Location),
                });
            }
        }

        return Common.Json(new
        {
            minimumSeverity = threshold.ToString().ToLowerInvariant(),
            count,
            truncated = count > MaxResults,
            diagnostics,
        });
    });

    [McpServerTool(Name = "find_dead_code")]
    [Description("Heuristically find private/internal members with no references (likely dead code). HEURISTIC: may include members used via reflection, DI, or serialization — review before deleting.")]
    public static Task<string> FindDeadCode(
        SolutionManager solutionManager,
        CancellationToken ct)
        => Common.Guarded("find_dead_code", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var compilations = await solutionManager.GetCompilationsAsync(ct);

        var candidates = new List<object>();
        var analyzed = 0;

        foreach (var (_, compilation) in compilations)
        {
            foreach (var type in Common.AllTypes(compilation.GlobalNamespace))
            {
                if (!type.InSource())
                {
                    continue;
                }

                foreach (var member in type.GetMembers())
                {
                    if (!IsDeadCodeCandidate(member))
                    {
                        continue;
                    }

                    if (analyzed >= MaxDeadCodeCandidates)
                    {
                        break;
                    }

                    analyzed++;
                    var references = await SymbolFinder.FindReferencesAsync(member, solution, ct);
                    var hasUse = references.Any(r => r.Locations.Any());
                    if (!hasUse)
                    {
                        candidates.Add(new
                        {
                            symbol = member.ToDisplayString(),
                            kind = Common.KindOf(member),
                            accessibility = member.DeclaredAccessibility.ToString().ToLowerInvariant(),
                            location = Common.Loc(member),
                        });
                    }
                }
            }
        }

        return Common.Json(new
        {
            note = "Heuristic. Members used via reflection, DI, or serialization may appear here. Review before deleting.",
            analyzed,
            analyzedCapReached = analyzed >= MaxDeadCodeCandidates,
            count = candidates.Count,
            candidates = candidates.Take(MaxResults),
        });
    });

    [McpServerTool(Name = "detect_circular_dependencies")]
    [Description("Detect circular dependencies between namespaces, based on type-signature references (base types, interfaces, field/property/parameter/return types). Returns the cycles found. Note: signature-level, not method-body level.")]
    public static Task<string> DetectCircularDependencies(
        SolutionManager solutionManager,
        CancellationToken ct)
        => Common.Guarded("detect_circular_dependencies", ct, async ct =>
    {
        var compilations = await solutionManager.GetCompilationsAsync(ct);

        // namespace -> set of namespaces it depends on (solution-internal only)
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var solutionNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, compilation) in compilations)
        {
            foreach (var type in Common.AllTypes(compilation.GlobalNamespace))
            {
                if (!type.InSource())
                {
                    continue;
                }

                var ns = type.ContainingNamespace?.ToDisplayString();
                if (string.IsNullOrEmpty(ns))
                {
                    continue;
                }

                solutionNamespaces.Add(ns);
                var deps = graph.TryGetValue(ns, out var existing) ? existing : graph[ns] = new(StringComparer.Ordinal);

                foreach (var referenced in ReferencedTypes(type))
                {
                    if (!referenced.InSource())
                    {
                        continue;
                    }

                    var refNs = referenced.ContainingNamespace?.ToDisplayString();
                    if (!string.IsNullOrEmpty(refNs) && refNs != ns)
                    {
                        deps.Add(refNs!);
                    }
                }
            }
        }

        // Keep only edges between solution namespaces.
        foreach (var key in graph.Keys.ToList())
        {
            graph[key].IntersectWith(solutionNamespaces);
        }

        var cycles = FindCycles(graph);

        return Common.Json(new
        {
            note = "Signature-level namespace dependencies (not method-body references).",
            namespaceCount = solutionNamespaces.Count,
            cycleCount = cycles.Count,
            cycles = cycles.Take(MaxResults),
        });
    });

    [McpServerTool(Name = "get_test_coverage_map")]
    [Description("Map which production types are referenced by test code (structural coverage), and list production types with no test reference. NOTE: structural, not line coverage — use /coverage for percentages.")]
    public static Task<string> GetTestCoverageMap(
        SolutionManager solutionManager,
        CancellationToken ct)
        => Common.Guarded("get_test_coverage_map", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var compilations = await solutionManager.GetCompilationsAsync(ct);

        var testProjectIds = new HashSet<ProjectId>();
        foreach (var project in solution.Projects)
        {
            if (IsTestProject(project))
            {
                testProjectIds.Add(project.Id);
            }
        }

        // Collect production type symbols referenced anywhere in test project code.
        var referencedByTests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (project, compilation) in compilations)
        {
            if (!testProjectIds.Contains(project.Id))
            {
                continue;
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct);
                foreach (var node in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbol = model.GetSymbolInfo(node, ct).Symbol;
                    var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
                    if (type is not null && type.InSource())
                    {
                        referencedByTests.Add(type.ToDisplayString());
                    }
                }
            }
        }

        // Walk production types and bucket them.
        var covered = new List<string>();
        var uncovered = new List<object>();
        foreach (var (project, compilation) in compilations)
        {
            if (testProjectIds.Contains(project.Id))
            {
                continue;
            }

            foreach (var type in Common.AllTypes(compilation.GlobalNamespace))
            {
                if (!type.InSource() || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                {
                    continue;
                }

                var name = type.ToDisplayString();
                if (referencedByTests.Contains(name))
                {
                    covered.Add(name);
                }
                else
                {
                    uncovered.Add(new { type = name, location = Common.Loc(type) });
                }
            }
        }

        var total = covered.Count + uncovered.Count;
        return Common.Json(new
        {
            note = "Structural: 'covered' = referenced by test code. Not line coverage — use /coverage for %.",
            testProjects = testProjectIds.Count,
            productionTypes = total,
            coveredCount = covered.Count,
            uncoveredCount = uncovered.Count,
            structuralCoveragePercent = total == 0 ? 0 : Math.Round(100.0 * covered.Count / total, 1),
            uncovered = uncovered.Take(MaxResults),
        });
    });

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsDeadCodeCandidate(ISymbol member)
    {
        if (member.IsImplicitlyDeclared || !member.InSource())
        {
            return false;
        }

        if (member.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Internal))
        {
            return false;
        }

        // Skip things commonly used indirectly.
        if (member.GetAttributes().Length > 0 || member.IsOverride || member.Name == "Main")
        {
            return false;
        }

        return member switch
        {
            IMethodSymbol m => m.MethodKind == MethodKind.Ordinary && !m.ExplicitInterfaceImplementations.Any(),
            IPropertySymbol p => !p.ExplicitInterfaceImplementations.Any(),
            IFieldSymbol f => !f.IsConst,
            IEventSymbol => true,
            _ => false,
        };
    }

    private static IEnumerable<INamedTypeSymbol> ReferencedTypes(INamedTypeSymbol type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (var i in type.Interfaces)
        {
            yield return i;
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol f when f.Type is INamedTypeSymbol ft:
                    yield return ft;
                    break;
                case IPropertySymbol p when p.Type is INamedTypeSymbol pt:
                    yield return pt;
                    break;
                case IMethodSymbol m:
                    if (m.ReturnType is INamedTypeSymbol rt)
                    {
                        yield return rt;
                    }

                    foreach (var param in m.Parameters)
                    {
                        if (param.Type is INamedTypeSymbol pt2)
                        {
                            yield return pt2;
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>Simple DFS cycle detection over a namespace dependency graph.</summary>
    private static List<List<string>> FindCycles(Dictionary<string, HashSet<string>> graph)
    {
        var cycles = new List<List<string>>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=unseen,1=in-stack,2=done
        var stack = new List<string>();

        void Visit(string node)
        {
            state[node] = 1;
            stack.Add(node);

            if (graph.TryGetValue(node, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    if (!state.TryGetValue(next, out var s) || s == 0)
                    {
                        Visit(next);
                    }
                    else if (s == 1)
                    {
                        var start = stack.IndexOf(next);
                        if (start >= 0)
                        {
                            var cycle = stack.Skip(start).ToList();
                            cycle.Add(next);
                            cycles.Add(cycle);
                        }
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
        }

        foreach (var node in graph.Keys)
        {
            if (!state.TryGetValue(node, out var s) || s == 0)
            {
                Visit(node);
            }
        }

        return cycles;
    }

    private static bool IsTestProject(Project project)
    {
        if (project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return project.MetadataReferences
            .Select(r => r.Display ?? string.Empty)
            .Any(d => d.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                   || d.Contains("nunit", StringComparison.OrdinalIgnoreCase)
                   || d.Contains("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
                   || d.Contains("mstest", StringComparison.OrdinalIgnoreCase));
    }
}
