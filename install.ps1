# Installs or updates proven-dotnet in ~/.claude (Windows PowerShell 5.1 or later). Safe to run again,
# and it never touches your own files or settings: see tools/layer.py.
# Extra arguments go to layer.py (e.g. --claude-dir DIR). Run with: powershell -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'tools/find-python.ps1')
$source = if ($env:PROVEN_SOURCE) { $env:PROVEN_SOURCE } else { Join-Path $here '.claude' }
& $script:Python @script:PythonArgs (Join-Path $here 'tools/layer.py') install $source @args
exit $LASTEXITCODE
