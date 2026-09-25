[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$payload = [System.IO.Path]::GetFullPath($PayloadDirectory)
$expectedModelHash = '1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B'

$requiredFiles = @(
    'WhisperMd.exe',
    'transcribe.ps1',
    'models\ggml-small.bin',
    'app\whisper.cpp\build-cpu\bin\Release\whisper-cli.exe',
    'app\whisper.cpp\build-vulkan\bin\Release\whisper-cli.exe',
    'VERSION.txt',
    'THIRD_PARTY_NOTICES.md'
)

foreach ($relative in $requiredFiles) {
    $path = Join-Path $payload $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing release file: $relative"
    }
}

$ffmpeg = Get-ChildItem -LiteralPath (Join-Path $payload 'tools\ffmpeg') -Recurse -File -Filter 'ffmpeg.exe' |
    Select-Object -First 1
if (-not $ffmpeg) {
    throw 'Missing FFmpeg executable in release payload.'
}

$model = Join-Path $payload 'models\ggml-small.bin'
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$modelStream = [System.IO.File]::OpenRead($model)
try {
    $modelHash = [System.BitConverter]::ToString($sha256.ComputeHash($modelStream)).Replace('-', '')
}
finally {
    $modelStream.Dispose()
    $sha256.Dispose()
}
if ($modelHash -ne $expectedModelHash) {
    throw "Release model hash mismatch: $modelHash"
}

if (Test-Path -LiteralPath (Join-Path $payload 'src')) {
    throw 'Source tree must not be present in release payload.'
}
if (Test-Path -LiteralPath (Join-Path $payload 'models\ggml-small.bin.part')) {
    throw 'Partial model file found in release payload.'
}
if (Test-Path -LiteralPath (Join-Path $payload 'tools\VulkanSDK')) {
    throw 'Vulkan SDK must not be bundled in the runtime payload.'
}
if (Test-Path -LiteralPath (Join-Path $payload 'tests')) {
    throw 'Test projects must not be bundled in the runtime payload.'
}

Write-Host 'PASS: release payload structure and model checksum.' -ForegroundColor Green
