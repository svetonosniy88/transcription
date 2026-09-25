$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\WhisperMd.Stage6Verifier\WhisperMd.Stage6Verifier.csproj'

Write-Host 'Running stage6 Markdown verifier...'
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Stage6 Markdown verifier failed with exit code $LASTEXITCODE."
}
