using System.ComponentModel;
using ProvenRoslynMcp.Workspace;
using ModelContextProtocol.Server;

namespace ProvenRoslynMcp.Tools;

/// <summary>Workspace state — answers instantly and never waits on, or starts, a load.</summary>
[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool(Name = "workspace_status")]
    [Description("Instant: is the solution loaded? Returns state (loading/ready/failed), the current load stage, projects loaded so far, the last project touched, and the error if it failed. Call this when another tool answered 'loading', instead of waiting.")]
    public static string WorkspaceStatus(SolutionManager solutionManager) =>
        Common.Json(solutionManager.Status());
}
