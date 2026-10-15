using System.ComponentModel;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using ProvenRoslynMcp.Workspace;

namespace ProvenRoslynMcp.Tools;

/// <summary>
/// The public API of a referenced package, read from the metadata the compiler already loaded: type and
/// member signatures with their XML summaries, the way IntelliSense shows them. Nothing is decompiled and
/// no method body is read, so this is the answer to "how do I call this library" that the rules allow.
/// </summary>
[McpServerToolType]
public static class PackageTools
{
    [McpServerTool(Name = "get_package_api")]
    [Description("Show the public API of a referenced package or assembly from its metadata: public and protected types and members with their signatures and XML summaries. Never decompiles. Use it before guessing how to call a library, instead of decompiling it.")]
    public static Task<string> GetPackageApi(
        SolutionManager solutionManager,
        [Description("Assembly name or its start, e.g. 'Microsoft.Extensions.Compliance.Redaction' or 'StackExchange.Redis'.")] string assembly,
        [Description("Optional type or namespace substring, e.g. 'Redactor'. Empty = every public type.")] string? typeFilter,
        [Description("Maximum types to return (default 40).")] int limit,
        CancellationToken ct)
        => Common.Guarded("get_package_api", ct, async ct =>
    {
        if (string.IsNullOrWhiteSpace(assembly))
        {
            return Common.Json(new { error = "Give an assembly name, e.g. 'StackExchange.Redis'." });
        }

        var compilations = await solutionManager.GetCompilationsAsync(ct);
        var referenced = new Dictionary<string, IAssemblySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, compilation) in compilations)
        {
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol symbol)
                {
                    referenced.TryAdd(symbol.Identity.GetDisplayName(), symbol);
                }
            }
        }

        var matches = referenced.Values
            .Where(a => a.Name.Equals(assembly, StringComparison.OrdinalIgnoreCase)
                     || a.Name.StartsWith(assembly + ".", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matches.Count == 0)
        {
            var near = referenced.Values.Select(a => a.Name)
                .Where(n => n.Contains(assembly, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order().Take(10).ToList();
            return Common.Json(new
            {
                assembly,
                found = false,
                hint = "No referenced assembly by that name. The package must be referenced by a project in the solution and restored.",
                near,
            });
        }

        var types = matches
            .SelectMany(a => Common.AllTypes(a.GlobalNamespace).Select(t => (Assembly: a, Type: t)))
            .Where(x => IsVisible(x.Type))
            .Where(x => string.IsNullOrWhiteSpace(typeFilter)
                     || x.Type.ToDisplayString().Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type.ToDisplayString(), StringComparer.Ordinal)
            .Select(x => new
            {
                name = x.Type.ToDisplayString(),
                kind = Common.KindOf(x.Type),
                assembly = x.Assembly.Name,
                summary = Summary(x.Type),
                members = x.Type.GetMembers()
                    .Where(m => IsVisible(m) && !m.IsImplicitlyDeclared && m is not INamedTypeSymbol
                             && m is not IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove })
                    .Select(m => new { kind = Common.KindOf(m), signature = m.ToDisplayString(), summary = Summary(m) })
                    .ToList(),
            });

        var (shown, total, more) = Common.Page(types, limit);
        return Common.Json(new
        {
            assemblies = matches.Select(a => a.Identity.GetDisplayName()).ToList(),
            filter = typeFilter,
            count = total,
            more,
            types = shown,
        });
    });

    private static bool IsVisible(ISymbol symbol) => symbol.DeclaredAccessibility is
        Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
        && (symbol.ContainingType is null || IsVisible(symbol.ContainingType));

    /// <summary>The XML doc summary as plain text, when the package ships its documentation file.</summary>
    private static string? Summary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var text = XElement.Parse(xml).Element("summary")?.Value;
            return string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        catch (System.Xml.XmlException)
        {
            return null;   // a malformed doc comment in a package is the package's problem, not a failed call
        }
    }
}
