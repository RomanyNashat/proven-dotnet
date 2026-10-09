# Finds Python 3 for the installers. Prefers the py launcher: "python" on a fresh Windows can be the
# Microsoft Store placeholder, which prints a message and exits instead of running anything.
$script:Python = $null
$script:PythonArgs = @()
if (Get-Command py -ErrorAction SilentlyContinue) {
    $script:Python = 'py'; $script:PythonArgs = @('-3')
} else {
    foreach ($candidate in @('python', 'python3')) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd -and $cmd.Source -notlike '*WindowsApps*') { $script:Python = $candidate; break }
    }
}
if (-not $script:Python) {
    Write-Host 'Python 3 is needed (the hooks use it too): https://www.python.org/downloads/'
    exit 1
}
