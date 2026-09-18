[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$InputPath,

    [ValidateSet('auto', 'vulkan', 'cpu')]
    [string]$Backend = 'auto',

    [string]$Language = 'ru',

    [ValidateRange(1, 32)]
    [int]$Threads = 8,

    [switch]$KeepWav
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$resolvedInput = (Resolve-Path -LiteralPath $InputPath).Path

if (-not (Test-Path -LiteralPath $resolvedInput -PathType Leaf)) {
    throw "Input file not found: $resolvedInput"
}

$model = Join-Path $root 'models\ggml-small.bin'
$cpuCli = Join-Path $root 'app\whisper.cpp\build-cpu\bin\Release\whisper-cli.exe'
$vulkanCli = Join-Path $root 'app\whisper.cpp\build-vulkan\bin\Release\whisper-cli.exe'

foreach ($requiredFile in @($model, $cpuCli)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required component not found: $requiredFile"
    }
}

$ffmpeg = Get-ChildItem -LiteralPath (Join-Path $root 'tools\ffmpeg') -Recurse -File -Filter 'ffmpeg.exe' |
    Select-Object -First 1

if (-not $ffmpeg) {
    throw 'FFmpeg was not found in tools\ffmpeg.'
}

$workingDir = Join-Path $root 'working'
$outputRoot = Join-Path $root 'output'
New-Item -ItemType Directory -Path $workingDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$stem = [System.IO.Path]::GetFileNameWithoutExtension($resolvedInput)
$safeStem = ($stem -replace '[<>:"/\\|?*]', '_').Trim()
if ([string]::IsNullOrWhiteSpace($safeStem)) {
    $safeStem = 'recording'
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$resultDir = Join-Path $outputRoot "$safeStem-$runId"
$wavPath = Join-Path $workingDir "$safeStem-$runId.wav"
New-Item -ItemType Directory -Path $resultDir | Out-Null
$outputPrefix = Join-Path $resultDir $safeStem

Write-Host "Preparing audio: $resolvedInput"
& $ffmpeg.FullName -hide_banner -loglevel error -y -i $resolvedInput -ar 16000 -ac 1 -c:a pcm_s16le $wavPath
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg failed with exit code $LASTEXITCODE. The source recording was preserved."
}

function Invoke-Whisper {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CliPath,

        [Parameter(Mandatory = $true)]
        [string]$BackendName
    )

    $arguments = @(
        '-m', $model,
        '-f', $wavPath,
        '-l', $Language,
        '-t', $Threads.ToString(),
        '-otxt',
        '-osrt',
        '-ojf',
        '-pp',
        '-of', $outputPrefix
    )

    if ($BackendName -eq 'cpu') {
        $arguments += '-ng'
    }

    Write-Host "Starting Whisper ($BackendName, language: $Language, threads: $Threads)..."
    & $CliPath @arguments | ForEach-Object { Write-Host $_ }
    $nativeExitCode = $LASTEXITCODE
    return [int]$nativeExitCode
}

$selectedBackend = $Backend
if ($selectedBackend -eq 'auto') {
    $selectedBackend = if (Test-Path -LiteralPath $vulkanCli -PathType Leaf) { 'vulkan' } else { 'cpu' }
}

$exitCode = if ($selectedBackend -eq 'vulkan') {
    if (-not (Test-Path -LiteralPath $vulkanCli -PathType Leaf)) {
        throw "Vulkan build not found: $vulkanCli"
    }
    Invoke-Whisper -CliPath $vulkanCli -BackendName 'vulkan'
} else {
    Invoke-Whisper -CliPath $cpuCli -BackendName 'cpu'
}

if ($exitCode -ne 0 -and $Backend -eq 'auto' -and $selectedBackend -eq 'vulkan') {
    Write-Warning "Vulkan failed with exit code $exitCode. Retrying on the CPU."
    $exitCode = Invoke-Whisper -CliPath $cpuCli -BackendName 'cpu'
    $selectedBackend = 'cpu'
}

if ($exitCode -ne 0) {
    throw "Whisper failed with exit code $exitCode. The source recording and temporary WAV were preserved."
}

$expectedOutputs = @(
    "$outputPrefix.txt",
    "$outputPrefix.srt",
    "$outputPrefix.json"
)

$missingOutputs = $expectedOutputs | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
if ($missingOutputs) {
    throw "Transcription completed without the expected files: $($missingOutputs -join ', ')"
}

if (-not $KeepWav) {
    Remove-Item -LiteralPath $wavPath -Force
}

Write-Host ''
Write-Host "Done. Backend: $selectedBackend"
Write-Host "Results: $resultDir"
Write-Host 'The source recording was not deleted.'
