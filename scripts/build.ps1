param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$RhinoSystemDir = 'C:\Program Files\Rhino 8\System'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
Push-Location $projectRoot
try {
    & $dotnetExe restore ModifierTree.sln --configfile NuGet.Config --disable-parallel "-p:RhinoSystemDir=$RhinoSystemDir"
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $dotnetExe build ModifierTree.sln --configuration $Configuration "-p:RhinoSystemDir=$RhinoSystemDir" --no-restore --disable-build-servers -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $pluginProject = [xml](Get-Content -LiteralPath src\ModifierTree.Rhino\ModifierTree.Rhino.csproj -Raw)
    $pluginVersion = [string]$pluginProject.Project.PropertyGroup.Version
    $pluginPath = Join-Path $projectRoot "artifacts\bin\$pluginVersion\$Configuration\net8.0-windows\ModifierTree.rhp"
    & $dotnetExe run --project tests\ModifierTree.Core.Checks --configuration $Configuration --no-build --no-restore -- $pluginPath
    if ($LASTEXITCODE -ne 0) { throw 'Registry or plugin metadata checks failed.' }
    Write-Host "Plugin: $pluginPath"
}
finally {
    Pop-Location
}
