# Removes proven-dotnet from ~/.claude: only the files and settings it installed.
# Run with: powershell -ExecutionPolicy Bypass -File uninstall.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'tools/find-python.ps1')
$id = if ($env:PROVEN_LAYER_ID) { $env:PROVEN_LAYER_ID } else { 'proven' }
& $script:Python @script:PythonArgs (Join-Path $here 'tools/layer.py') uninstall $id @args
exit $LASTEXITCODE
