# Tries this download without touching your real ~/.claude, and writes everything it saw to
# try-first-report.txt next to this script. Send that file back if anything looks wrong.
#   1. which Python the installers will use
#   2. what moving an old harness aside would do, when this download can (a dry run: nothing changes)
#   3. an install into a throw-away folder, what it put there, and the uninstall
# Works for proven-dotnet and for a team release built on it. Exit code 1 if a step failed.
# Run with: powershell -ExecutionPolicy Bypass -File try-first.ps1
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$report = Join-Path $here 'try-first-report.txt'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('proven-try-first-' + [Guid]::NewGuid().ToString('N'))
$claudeDir = Join-Path $scratch '.claude'
$self = (Get-Process -Id $PID).Path   # the same PowerShell for the installers
$failed = @()

function Write-Report([string]$text) {
    Write-Host $text
    Add-Content -Path $report -Value $text -Encoding UTF8
}

function Invoke-Step([string]$title, [scriptblock]$step) {
    Write-Report ''
    Write-Report ('== ' + $title)
    $global:LASTEXITCODE = 0
    $output = & $step 2>&1 | Out-String
    Write-Report $output.TrimEnd()
    if ($LASTEXITCODE -ne 0) {
        Write-Report ('!! exit code ' + $LASTEXITCODE)
        $script:failed += $title
    }
}

Set-Content -Path $report -Value ('try-first ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') + ', version ' + (Get-Content (Join-Path $here 'VERSION'))) -Encoding UTF8
Write-Report ('PowerShell ' + $PSVersionTable.PSVersion + ' on ' + [Environment]::OSVersion.VersionString)

$finder = @('tools/find-python.ps1', 'core/tools/find-python.ps1') | ForEach-Object { Join-Path $here $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
. $finder
Invoke-Step 'Python' { & $script:Python @script:PythonArgs --version }

$migrate = Join-Path $here 'migrate-from-ecc.py'
if (Test-Path $migrate) {
    Invoke-Step 'Moving the old harness aside: dry run on your real folder (changes nothing)' {
        & $script:Python @script:PythonArgs $migrate --dry-run
    }
}

$env:PROVEN_CLAUDE_DIR = $claudeDir
try {
    Invoke-Step ('Install into ' + $claudeDir) {
        & $self -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'install.ps1')
    }
    Invoke-Step 'What it installed' {
        $registry = Join-Path $claudeDir '.layers/registry.json'
        if (Test-Path $registry) {
            $layers = (Get-Content $registry -Raw | ConvertFrom-Json).layers
            foreach ($id in $layers.PSObject.Properties.Name) {
                '{0} {1}: {2} files' -f $id, $layers.$id.version, @($layers.$id.files).Count
            }
        } else {
            'registry: missing'
            $global:LASTEXITCODE = 1
        }
        foreach ($part in @('rules', 'skills', 'agents', 'commands', 'hooks')) {
            $path = Join-Path $claudeDir $part
            $count = 0
            if (Test-Path $path) { $count = @(Get-ChildItem -Force $path -Recurse -File).Count }
            '{0}: {1} files' -f $part, $count
        }
        $settings = Join-Path $claudeDir 'settings.json'
        if (Test-Path $settings) {
            $null = Get-Content $settings -Raw | ConvertFrom-Json
            'settings.json: valid JSON'
        } else {
            'settings.json: missing'
            $global:LASTEXITCODE = 1
        }
    }
    Invoke-Step 'Uninstall' {
        & $self -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'uninstall.ps1')
    }
    Invoke-Step 'Left behind after uninstall (settings.json and the empty registry at most)' {
        # -Force: on macOS and Linux a folder whose name starts with a dot is hidden.
        $item = Get-Item -Force $claudeDir -ErrorAction SilentlyContinue
        if ($item) {
            $base = $item.FullName
            Get-ChildItem -Force $base -Recurse -File | ForEach-Object { $_.FullName.Substring($base.Length + 1) }
            $left = @(Get-ChildItem -Force (Join-Path $base 'skills'), (Join-Path $base 'rules') -Recurse -File -ErrorAction SilentlyContinue)
            if ($left.Count -gt 0) { $global:LASTEXITCODE = 1 }
        }
    }
}
finally {
    Remove-Item Env:PROVEN_CLAUDE_DIR -ErrorAction SilentlyContinue
    if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
}

Write-Report ''
if ($failed.Count -eq 0) {
    Write-Report 'All steps finished. Your real ~/.claude was not changed.'
} else {
    Write-Report ('Steps that failed: ' + ($failed -join '; '))
}
Write-Host ''
Write-Host ('Report: ' + $report)
if ($failed.Count -gt 0) { exit 1 }
exit 0
