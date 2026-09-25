[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

Write-Host '1/3 Build WPF...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'build-wpf.ps1')
if ($LASTEXITCODE -ne 0) { throw "WPF build failed: $LASTEXITCODE" }

Write-Host '2/3 Verify Markdown stage6...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'verify-markdown-stage6.ps1')
if ($LASTEXITCODE -ne 0) { throw "Stage6 verifier failed: $LASTEXITCODE" }

Write-Host '3/3 Verify history/settings stage7-9...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'verify-stage7-9.ps1')
if ($LASTEXITCODE -ne 0) { throw "Stage7-9 verifier failed: $LASTEXITCODE" }

Write-Host ''
Write-Host 'PASS: build + stage6 + stage7-9 verifiers' -ForegroundColor Green
