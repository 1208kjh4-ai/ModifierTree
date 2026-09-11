param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Save your work and close all Rhino windows before installing. No files or registration values were changed.'
}
$pluginProject = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\ModifierTree.Rhino\ModifierTree.Rhino.csproj') -Raw)
$pluginVersion = [string]$pluginProject.Project.PropertyGroup.Version
$sourceDir = Join-Path $projectRoot "artifacts\bin\$pluginVersion\$Configuration\net8.0-windows"
$sourcePlugin = Join-Path $sourceDir 'ModifierTree.rhp'
if (-not (Test-Path -LiteralPath $sourcePlugin -PathType Leaf)) { throw "Build the plugin first: $sourcePlugin" }
$assemblyName = [Reflection.AssemblyName]::GetAssemblyName($sourcePlugin)
if ($assemblyName.Name -ne 'ModifierTree' -or $assemblyName.Version.ToString(3) -ne $pluginVersion) {
    throw 'The build artifact does not match the project version.'
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'ModifierTree.Core.dll') -PathType Leaf)) {
    throw 'The build is missing ModifierTree.Core.dll.'
}

$installDir = Join-Path $projectRoot 'artifacts\installed\ModifierTree'
$installedPlugin = Join-Path $installDir 'ModifierTree.rhp'
$registryKey = 'HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-ins\df5c138d-8bf7-4fab-adaa-c25c53baa6e9\PlugIn'
$registered = Test-Path -LiteralPath $registryKey
$previousPath = if ($registered) { (Get-ItemProperty -LiteralPath $registryKey -Name FileName).FileName } else { $null }

# Keep both the prior binaries and the original registration path for recovery.
$backupDir = Join-Path $projectRoot ('artifacts\install-backups\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
@{ Registered = $registered; RegistryKey = $registryKey; PreviousPath = $previousPath; Version = $pluginVersion } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupDir 'registration.json') -Encoding UTF8
$previousFiles = @()
if (Test-Path -LiteralPath $installDir) {
    $previousFiles = @(Get-ChildItem -LiteralPath $installDir -File)
    foreach ($file in $previousFiles) { Copy-Item -LiteralPath $file.FullName -Destination $backupDir }
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
try {
    foreach ($file in Get-ChildItem -LiteralPath $sourceDir -File) {
        Copy-Item -LiteralPath $file.FullName -Destination $installDir -Force
    }
    if ($registered) {
        # Update only this plugin's load path; preserve its ID, settings and other plugins.
        Set-ItemProperty -LiteralPath $registryKey -Name FileName -Value $installedPlugin
        $actual = (Get-ItemProperty -LiteralPath $registryKey -Name FileName).FileName
        if ($actual -ne $installedPlugin) { throw 'The plugin registration could not be verified.' }
    }
}
catch {
    foreach ($file in $previousFiles) {
        Copy-Item -LiteralPath (Join-Path $backupDir $file.Name) -Destination $installDir -Force
    }
    # Existing registration remains unchanged if its write was denied.
    throw
}
Write-Host "Installed ModifierTree $pluginVersion to $installedPlugin"
Write-Host "Previous installation backup: $backupDir"
if ($registered) {
    Write-Host 'Start Rhino and run MTreeStatus. Do not drag another version-specific .rhp into Rhino.'
}
else {
    Write-Host "Start Rhino and drag this file into Rhino once: $installedPlugin"
}
