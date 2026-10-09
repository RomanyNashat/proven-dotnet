# End to end through the real Windows wrappers: install the fixture layer into a sandbox that already
# holds the user's own files and settings, then uninstall, and check the user's things are as before.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sb = Join-Path ([IO.Path]::GetTempPath()) ('proven-e2e-' + [Guid]::NewGuid().ToString('N') + '\.claude')
New-Item -ItemType Directory -Force (Join-Path $sb 'skills\mine') | Out-Null
Set-Content -Path (Join-Path $sb 'skills\mine\SKILL.md') -Value 'mine' -Encoding ASCII
Set-Content -Path (Join-Path $sb 'settings.json') -Value '{"model": "opus"}' -Encoding ASCII
function Fail([string]$why) { Write-Host "::error title=installer e2e (PowerShell)::$why"; exit 1 }

$env:PROVEN_SOURCE = Join-Path $root 'tests\installer\fixture\.claude'
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'install.ps1') --claude-dir $sb
if ($LASTEXITCODE -ne 0) { Fail "install.ps1 exited $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $sb 'rules\proven-fixture\core.md'))) { Fail 'core.md not installed as a rule' }
if (-not (Test-Path (Join-Path $sb 'skills\demo\SKILL.md'))) { Fail 'skill not installed' }
$settings = Get-Content (Join-Path $sb 'settings.json') -Raw
if ($settings -notmatch 'hooks/proven-fixture/hello.py') { Fail 'hook not merged into settings.json' }
if ($settings -notmatch '"opus"') { Fail "the user's model setting was lost" }

$env:PROVEN_LAYER_ID = 'proven-fixture'
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'uninstall.ps1') --claude-dir $sb
if ($LASTEXITCODE -ne 0) { Fail "uninstall.ps1 exited $LASTEXITCODE" }
$base = (Get-Item $sb).FullName   # one form of the path for both sides (the temp dir can be an 8.3 name)
$left = (Get-ChildItem $base -Recurse -File | Where-Object { $_.FullName -notlike '*\.layers\*' } |
    ForEach-Object { $_.FullName.Substring($base.Length + 1) } | Sort-Object) -join ' '
if ($left -ne 'settings.json skills\mine\SKILL.md') { Fail "after uninstall: $left" }
$after = Get-Content (Join-Path $sb 'settings.json') -Raw | ConvertFrom-Json
if ($after.model -ne 'opus' -or @($after.PSObject.Properties).Count -ne 1) { Fail "settings.json after uninstall: $(Get-Content (Join-Path $sb 'settings.json') -Raw)" }
Write-Host 'installer e2e (PowerShell): ok'
