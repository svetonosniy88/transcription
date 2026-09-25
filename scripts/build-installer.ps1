[CmdletBinding()]
param(
    [string]$IsccPath = '',
    [switch]$SkipPayloadBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root 'release\payload'
$releaseDir = Join-Path $root 'release'
$iss = Join-Path $root 'installer\WhisperMd.iss'

if (-not $SkipPayloadBuild) {
    & (Join-Path $PSScriptRoot 'build-release-payload.ps1') -Destination $payload
    if ($LASTEXITCODE -ne 0) { throw "Payload build failed: $LASTEXITCODE" }
}
else {
    & (Join-Path $PSScriptRoot 'verify-release-payload.ps1') -PayloadDirectory $payload
    if ($LASTEXITCODE -ne 0) { throw "Payload verification failed: $LASTEXITCODE" }
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    }

    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw @'
Inno Setup 6 compiler (ISCC.exe) was not found.
Install Inno Setup 6, for example with:
  winget install --id JRSoftware.InnoSetup -e
Then rerun scripts\build-installer.ps1.
'@
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$sourceDefine = '/DSourceDir=' + $payload
$outputDefine = '/DOutputDir=' + $releaseDir
$versionDefine = '/DAppVersion=1.0.0'

Write-Host 'Building Inno Setup installer...' -ForegroundColor Cyan
& $IsccPath $sourceDefine $outputDefine $versionDefine $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$installer = Join-Path $releaseDir 'WhisperMd-Setup-1.0.0.exe'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer was not produced: $installer"
}

Write-Host ''
Write-Host 'Installer ready.' -ForegroundColor Green
Write-Host $installer
Write-Host ('Size: {0:N2} MB' -f ((Get-Item -LiteralPath $installer).Length / 1MB))
