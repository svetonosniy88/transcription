[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$candidates = @(
    (Join-Path $projectRoot 'releases\current\WhisperMd.exe'),
    (Join-Path $projectRoot 'releases\WhisperMd-v0.0.1\WhisperMd.exe')
)

$executable = $candidates | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
} | Select-Object -First 1

if (-not $executable) {
    throw 'WhisperMd executable was not found. Run scripts\build-gui.ps1 first.'
}

Start-Process -FilePath $executable -WorkingDirectory $projectRoot
