using System.ComponentModel;
using ProvenRoslynMcp.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using ModelContextProtocol.Server;

namespace ProvenRoslynMcp.Tools;

/// <summary>
/// Refactoring and test-aware tools.
///
/// rename_symbol renames a symbol across the whole solution using Roslyn's Renamer (which updates
/// every reference correctly, unlike text find/replace). It supports a preview mode (compute the
/// changes and return a diff summary WITHOUT writing) and, when applied, writes atomically with
/// rollback — every changed file is written, and if any single write fails, all already-written
/// files are restored from their originals, so the working tree is never left half-renamed.
///
/// find_tests_for_symbol answers "what tests exercise this production symbol?" by finding references
/// to the symbol that live in test projects — used by the parity gate to know what already covers a
/// symbol before it changes.
///
/// NOTE: this is server C# — it compiles on the developer's machine (the install rebuilds the
/// Roslyn tool). It is not compiled or tested in a packaging environment.
/// </summary>
[McpServerToolType]
public static class RefactorTools
{
    [McpServerTool(Name = "rename_symbol")]
    [Description(
        "Rename a symbol across the whole solution using Roslyn (updates every reference correctly). " +
        "Set preview=true (default) to compute the change and return a diff summary WITHOUT writing. " +
        "Set preview=false to apply: files are written atomically — if any write fails, all changes " +
        "are rolled back so the tree is never left half-renamed. Prefer this over text find/replace.")]
    public static Task<string> RenameSymbol(
        SolutionManager solutionManager,
        [Description("Current symbol name. Simple ('CanonicalId') or qualified ('Orders.CanonicalId').")] string name,
        [Description("New name for the symbol.")] string newName,
        [Description("If true (default), compute and return the diff without writing. If false, apply the rename.")] bool preview,
        CancellationToken ct)
        => Common.Guarded("rename_symbol", ct, async ct =>
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Common.Json(new { error = "newName is required." });
        }

        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = await Common.ResolveAsync(solution, name, ct);

        if (symbols.Count == 0)
        {
            return Common.Json(new { query = name, error = "Symbol not found." });
        }
        if (symbols.Count > 1)
        {
            // Ambiguous — make the caller disambiguate rather than renaming the wrong thing.
            return Common.Json(new
            {
                query = name,
                error = "Ambiguous symbol — more than one match. Qualify the name.",
                candidates = symbols.Take(20).Select(s => new
                {
                    name = s.ToDisplayString(),
                    kind = Common.KindOf(s),
                    location = Common.Loc(s),
                }),
            });
        }

        var symbol = symbols[0];

        // Compute the renamed solution (does not touch disk).
        Solution renamed;
        try
        {
            renamed = await Renamer.RenameSymbolAsync(
                solution, symbol, new SymbolRenameOptions(), newName, ct);
        }
        catch (Exception ex)
        {
            return Common.Json(new { query = name, error = "Rename failed to compute: " + ex.Message });
        }

        // Diff the changed documents.
        var changes = solution.GetChanges(renamed);
        var changedDocs = new List<(DocumentId Id, string Path, string OldText, string NewText)>();

        foreach (var projectChange in changes.GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = solution.GetDocument(docId);
                var newDoc = renamed.GetDocument(docId);
                if (oldDoc?.FilePath is null || newDoc is null) continue;

                var oldText = (await oldDoc.GetTextAsync(ct)).ToString();
                var newText = (await newDoc.GetTextAsync(ct)).ToString();
                if (oldText != newText)
                {
                    changedDocs.Add((docId, oldDoc.FilePath, oldText, newText));
                }
            }
        }

        var fileSummaries = changedDocs.Select(d => new
        {
            file = d.Path,
            changedLines = CountChangedLines(d.OldText, d.NewText),
        }).ToList();

        // Preview mode: report what WOULD change, write nothing.
        if (preview)
        {
            return Common.Json(new
            {
                symbol = symbol.ToDisplayString(),
                kind = Common.KindOf(symbol),
                oldName = symbol.Name,
                newName,
                preview = true,
                filesAffected = fileSummaries.Count,
                files = fileSummaries,
                note = "Preview only — nothing written. Call again with preview=false to apply.",
                publicContractWarning = symbol.DeclaredAccessibility == Accessibility.Public
                    ? "Symbol is PUBLIC — in-solution references are updated, but published/cross-service consumers are NOT visible to Roslyn. Coordinate/version this rename."
                    : null,
            });
        }

        // Apply mode: write atomically with rollback.
        var written = new List<(string Path, string OriginalText)>();
        try
        {
            foreach (var d in changedDocs)
            {
                var original = await File.ReadAllTextAsync(d.Path, ct);
                await File.WriteAllTextAsync(d.Path, d.NewText, ct);
                written.Add((d.Path, original));
            }
        }
        catch (Exception ex)
        {
            // Roll back everything already written.
            var rollbackErrors = new List<string>();
            foreach (var (path, originalText) in written)
            {
                try { await File.WriteAllTextAsync(path, originalText, CancellationToken.None); }
                catch (Exception rex) { rollbackErrors.Add($"{path}: {rex.Message}"); }
            }

            return Common.Json(new
            {
                symbol = symbol.ToDisplayString(),
                newName,
                applied = false,
                error = "Write failed; all changes rolled back: " + ex.Message,
                rollbackErrors = rollbackErrors.Count == 0 ? null : rollbackErrors,
            });
        }

        return Common.Json(new
        {
            symbol = symbol.ToDisplayString(),
            oldName = symbol.Name,
            newName,
            applied = true,
            filesWritten = written.Count,
            files = fileSummaries,
            note = "Applied. The workspace cache is stale until the next load; re-open the solution if you continue analyzing.",
        });
    });

    [McpServerTool(Name = "find_tests_for_symbol")]
    [Description(
        "Find the tests that exercise a production symbol: references to the symbol that live in test " +
        "projects (projects whose name ends in Test/Tests, or that reference a test framework). Used to " +
        "know what already covers a symbol before changing it (the parity gate's 'what covers this?').")]
    public static Task<string> FindTestsForSymbol(
        SolutionManager solutionManager,
        [Description("Symbol name to find covering tests for.")] string name,
        CancellationToken ct)
        => Common.Guarded("find_tests_for_symbol", ct, async ct =>
    {
        var solution = await solutionManager.GetSolutionAsync(ct);
        var symbols = await Common.ResolveAsync(solution, name, ct);

        if (symbols.Count == 0)
        {
            return Common.Json(new { query = name, count = 0, tests = Array.Empty<object>() });
        }

        var testProjectIds = solution.Projects
            .Where(IsTestProject)
            .Select(p => p.Id)
            .ToHashSet();

        var hits = new List<object>();
        var seen = new HashSet<string>();

        foreach (var symbol in symbols)
        {
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
            foreach (var r in references)
            {
                foreach (var loc in r.Locations)
                {
                    var docProjectId = loc.Document.Project.Id;
                    if (!testProjectIds.Contains(docProjectId)) continue;

                    var span = loc.Location.GetLineSpan();
                    var key = span.Path + ":" + span.StartLinePosition.Line;
                    if (!seen.Add(key)) continue;

                    hits.Add(new
                    {
                        testProject = loc.Document.Project.Name,
                        location = Common.FromLocation(loc.Location),
                    });
                }
            }
        }

        return Common.Json(new
        {
            query = name,
            count = hits.Count,
            covered = hits.Count > 0,
            tests = hits,
            note = hits.Count == 0
                ? "No test-project references found — this symbol appears uncovered. The parity gate should characterize it before changing."
                : null,
        });
    });

    // A project counts as a test project if its name ends in Test/Tests, or it references a known
    // test framework assembly (xunit, nunit, mstest).
    private static bool IsTestProject(Project project)
    {
        var n = project.Name;
        if (n.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return project.MetadataReferences.Any(m =>
        {
            var d = m.Display ?? string.Empty;
            return d.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                || d.Contains("nunit", StringComparison.OrdinalIgnoreCase)
                || d.Contains("mstest", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static int CountChangedLines(string oldText, string newText)
    {
        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');
        int changed = 0;
        int max = Math.Max(oldLines.Length, newLines.Length);
        for (int i = 0; i < max; i++)
        {
            var o = i < oldLines.Length ? oldLines[i] : null;
            var nw = i < newLines.Length ? newLines[i] : null;
            if (o != nw) changed++;
        }
        return changed;
    }
}
