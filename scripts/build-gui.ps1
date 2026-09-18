[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$FrameworkDependent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\WhisperMd\WhisperMd.csproj'
$output = Join-Path $projectRoot 'releases\current'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project not found: $project"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$arguments = @(
    'publish',
    $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', $selfContained,
    '-o', $output,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None'
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output 'WhisperMd.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Build completed without the expected file: $executable"
}

Write-Host "Done: $executable"
