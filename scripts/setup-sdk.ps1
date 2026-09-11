param([string]$Version = '8.0.424')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $projectRoot '.tools'
$installer = Join-Path $toolsDir 'dotnet-install.ps1'
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'The .NET installer must have a valid Microsoft signature.'
}
& powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Version $Version -InstallDir (Join-Path $toolsDir 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw '.NET SDK installation failed.' }
