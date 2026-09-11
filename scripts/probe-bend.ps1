param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$output = Join-Path $projectRoot 'artifacts\bend-probe'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Push-Location $projectRoot
try {
    & $dotnetExe build prototypes\ModifierTree.Bend.Probe\ModifierTree.Bend.Probe.csproj -c Release -v minimal 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Bend prototype build failed.' }
    if ($BuildOnly) { return }
    # Windows PowerShell wraps native stderr in ErrorRecord. Keep the entire exception
    # in the log and use the process exit code, rather than terminating on its first line.
    $ErrorActionPreference = 'Continue'
    & $dotnetExe prototypes\ModifierTree.Bend.Probe\bin\Release\net8.0-windows\ModifierTree.Bend.Probe.dll $output 2>&1 |
        Tee-Object -FilePath (Join-Path $output 'run.log')
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { throw 'Bend prototype did not pass. See artifacts\bend-probe\run.log.' }
}
finally { Pop-Location }
