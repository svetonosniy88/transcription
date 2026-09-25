param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\WhisperMd.Stage789Verifier\WhisperMd.Stage789Verifier.csproj'

Write-Host 'Running stage 7-9 verifier...' -ForegroundColor Cyan
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Stage 7-9 verifier failed with exit code $LASTEXITCODE"
}
