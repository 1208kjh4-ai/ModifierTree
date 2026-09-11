param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$RhinoSystemDir = 'C:\Program Files\Rhino 8\System',
    [switch]$UIOnly,
    [switch]$BendOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
Push-Location $projectRoot
try {
    $checkArgs = @('run', '--project', 'tests\ModifierTree.Rhino.Checks', '--configuration', $Configuration, '--no-build', '--no-restore', '--', $RhinoSystemDir)
    if ($UIOnly) { $checkArgs += '--ui-only' }
    if ($BendOnly) { $checkArgs += '--bend-only' }
    & $dotnetExe @checkArgs
    if ($LASTEXITCODE -ne 0) { throw 'Rhino host checks failed.' }
}
finally { Pop-Location }
