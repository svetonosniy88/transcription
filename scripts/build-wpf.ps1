[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\WhisperMd.App\WhisperMd.App.csproj'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "WPF project not found: $project"
}

& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

Write-Host 'WPF shell build completed.'
