# Removes proven-dotnet from ~/.claude: only the files and settings it installed.
# PROVEN_CLAUDE_DIR overrides ~/.claude. Run with: powershell -ExecutionPolicy Bypass -File uninstall.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'tools/find-python.ps1')
$id = if ($env:PROVEN_LAYER_ID) { $env:PROVEN_LAYER_ID } else { 'proven' }
$dir = @()
if ($env:PROVEN_CLAUDE_DIR) { $dir = @('--claude-dir', $env:PROVEN_CLAUDE_DIR) }
& $script:Python @script:PythonArgs (Join-Path $here 'tools/layer.py') uninstall $id @dir @args
exit $LASTEXITCODE
