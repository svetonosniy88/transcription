[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$checks = [System.Collections.Generic.List[object]]::new()

function Add-FileCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    $checks.Add([pscustomobject]@{
        Component = $Name
        Status = if ($exists) { 'OK' } else { 'MISSING' }
        Path = $Path
    })
}

$model = Join-Path $projectRoot 'models\ggml-small.bin'
$cpuCli = Join-Path $projectRoot 'app\whisper.cpp\build-cpu\bin\Release\whisper-cli.exe'
$vulkanCli = Join-Path $projectRoot 'app\whisper.cpp\build-vulkan\bin\Release\whisper-cli.exe'
$sourceProject = Join-Path $projectRoot 'src\WhisperMd\WhisperMd.csproj'
$transcribeScript = Join-Path $projectRoot 'transcribe.ps1'
$currentGui = Join-Path $projectRoot 'releases\current\WhisperMd.exe'
$ffmpeg = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tools\ffmpeg') -Recurse -File -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1

Add-FileCheck -Name 'Whisper model (small)' -Path $model
Add-FileCheck -Name 'whisper.cpp CPU' -Path $cpuCli
Add-FileCheck -Name 'whisper.cpp Vulkan' -Path $vulkanCli
Add-FileCheck -Name 'FFmpeg' -Path $(if ($ffmpeg) { $ffmpeg.FullName } else { Join-Path $projectRoot 'tools\ffmpeg\ffmpeg.exe' })
Add-FileCheck -Name 'Transcription script' -Path $transcribeScript
Add-FileCheck -Name 'WhisperMd source' -Path $sourceProject
Add-FileCheck -Name 'WhisperMd release' -Path $currentGui

$expectedModelHash = '1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B'
if (Test-Path -LiteralPath $model -PathType Leaf) {
    $actualModelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $model).Hash
    $checks.Add([pscustomobject]@{
        Component = 'Model SHA-256'
        Status = if ($actualModelHash -eq $expectedModelHash) { 'OK' } else { 'MISMATCH' }
        Path = $actualModelHash
    })
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$checks.Add([pscustomobject]@{
    Component = '.NET SDK'
    Status = if ($dotnet) { 'OK' } else { 'MISSING' }
    Path = if ($dotnet) { (& dotnet --version) } else { 'dotnet not found' }
})

$checks | Format-Table -AutoSize

$failed = $checks | Where-Object { $_.Status -ne 'OK' }
if ($failed) {
    throw 'Environment check failed.'
}

Write-Host ''
Write-Host 'The environment is ready for development and transcription.'
