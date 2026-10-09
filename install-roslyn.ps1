<#
.SYNOPSIS
  Build and install the proven-dotnet Roslyn MCP server as a global .NET tool (Windows / PowerShell).

.DESCRIPTION
  Builds, packs, and installs (or updates) the proven-roslyn-mcp global tool. Can be run on its own
  to rebuild the server without touching the rest of the harness.

  Written for Windows PowerShell 5.1 compatibility (no try/catch/finally blocks).

.PARAMETER ProjectDir
  Path to the tools/proven-roslyn-mcp folder. Defaults to the one next to this script.

.EXAMPLE
  .\install-roslyn.ps1
  powershell -ExecutionPolicy Bypass -File .\install-roslyn.ps1
#>
[CmdletBinding()]
param(
    [string]$ProjectDir = (Join-Path $PSScriptRoot 'tools\proven-roslyn-mcp')
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "-> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  + $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  ! $msg" -ForegroundColor Yellow }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Warn2 ".NET SDK not found. Install the .NET SDK, then re-run."
    exit 1
}
if (-not (Test-Path $ProjectDir)) {
    Write-Warn2 "Roslyn project not found at $ProjectDir"
    exit 1
}

$startDir = Get-Location
Set-Location $ProjectDir

Write-Step "Building Roslyn MCP server"
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Set-Location $startDir
    Write-Warn2 "Build failed. The MCP SDK is in preview - you may need to bump a package version"
    Write-Warn2 "(dotnet add package ModelContextProtocol) and retry. See README.md."
    exit 1
}

Write-Step "Packing tool"
$nupkg = Join-Path $ProjectDir 'nupkg'
dotnet pack -c Release -o $nupkg
if ($LASTEXITCODE -ne 0) {
    Set-Location $startDir
    Write-Warn2 "Pack failed. See the error above."
    exit 1
}

Write-Step "Installing global tool 'proven-roslyn-mcp'"

# The server is a running process while Claude Code is open, and Windows will not let
# a locked executable be replaced: the uninstall fails with
#   Access to the path '...\.dotnet\tools\.store\proven.roslyn.mcp\<ver>' is denied.
# That is why a "successful" rebuild could still leave the OLD binary installed.
# Stop it first; Claude Code respawns the server on restart.
$procs = Get-Process -Name 'proven-roslyn-mcp' -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "  ! proven-roslyn-mcp is running ($($procs.Count) process(es)) and holds the files open." -ForegroundColor Yellow
    Write-Host "    Claude Code starts it automatically; stopping it now is safe and it will come back on restart."
    $answer = Read-Host "    Stop it and continue? [Y/n]"
    if ($answer -eq '' -or $answer -match '^[Yy]') {
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2   # let Windows release the file handles
        Write-Ok "Stopped the running server"
    } else {
        Write-Warn2 "Skipping the tool install - the running server would block it."
        Write-Warn2 "Close Claude Code completely, then re-run this script."
        Set-Location $startDir
        exit 1
    }
}

$installed = (dotnet tool list --global | Select-String -SimpleMatch 'proven.roslyn.mcp')
if ($installed) {
    # Force-replace. `dotnet tool update` is a no-op when the package version is
    # unchanged, which silently keeps the OLD binary after a successful rebuild.
    dotnet tool uninstall --global Proven.Roslyn.Mcp | Out-Null
    dotnet tool install --global --add-source $nupkg Proven.Roslyn.Mcp
} else {
    dotnet tool install --global --add-source $nupkg Proven.Roslyn.Mcp
}

Set-Location $startDir

if ($LASTEXITCODE -ne 0) {
    Write-Warn2 "Tool install failed. See the error above."
    Write-Warn2 "If it says 'Access to the path ... is denied', the server is still running."
    Write-Warn2 "Close Claude Code COMPLETELY (all windows), then re-run this script."
    exit 1
}

Write-Ok "proven-roslyn-mcp installed"
Write-Host ""
# Registration is ONCE, for every project (user scope, stored in ~/.claude.json) - not per project.
$claudeJson = Join-Path $HOME ".claude.json"
if ((Test-Path $claudeJson) -and (Select-String -Path $claudeJson -Pattern '"proven-roslyn"' -SimpleMatch -Quiet)) {
    Write-Ok "proven-roslyn is already registered with Claude Code - nothing to do per project. Restart Claude Code."
} else {
    Write-Warn2 "One-time step: register it for every project, then restart Claude Code:"
    Write-Warn2 "  claude mcp add --scope user --transport stdio proven-roslyn proven-roslyn-mcp"
    Write-Warn2 "(A repo's own .mcp.json is only for overriding settings in that one repo.)"
}
