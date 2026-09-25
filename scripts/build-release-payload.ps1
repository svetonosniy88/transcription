[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$Destination = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $root 'release\payload'
}

$project = Join-Path $root 'src\WhisperMd.App\WhisperMd.App.csproj'
$model = Join-Path $root 'models\ggml-small.bin'
$cpuDir = Join-Path $root 'app\whisper.cpp\build-cpu\bin\Release'
$vulkanDir = Join-Path $root 'app\whisper.cpp\build-vulkan\bin\Release'
$transcribe = Join-Path $root 'transcribe.ps1'
$expectedModelHash = '1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B'

foreach ($required in @($project, $model, $cpuDir, $vulkanDir, $transcribe)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Release dependency not found: $required"
    }
}

$ffmpegExe = Get-ChildItem -LiteralPath (Join-Path $root 'tools\ffmpeg') -Recurse -File -Filter 'ffmpeg.exe' |
    Select-Object -First 1
if (-not $ffmpegExe) {
    throw 'FFmpeg executable not found under tools\ffmpeg.'
}

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
    throw "Unexpected ggml-small.bin SHA-256. Expected $expectedModelHash, got $modelHash"
}

$publishTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('WhisperMd-publish-' + [Guid]::NewGuid().ToString('N'))
try {
    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    New-Item -ItemType Directory -Force -Path $publishTemp | Out-Null

    Write-Host 'Publishing self-contained WPF application...' -ForegroundColor Cyan
    & dotnet publish $project `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishTemp
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Copy-Item -Path (Join-Path $publishTemp '*') -Destination $Destination -Recurse -Force
    Copy-Item -LiteralPath $transcribe -Destination (Join-Path $Destination 'transcribe.ps1') -Force

    $modelDest = Join-Path $Destination 'models'
    New-Item -ItemType Directory -Force -Path $modelDest | Out-Null
    Copy-Item -LiteralPath $model -Destination (Join-Path $modelDest 'ggml-small.bin') -Force

    $cpuDest = Join-Path $Destination 'app\whisper.cpp\build-cpu\bin\Release'
    $vulkanDest = Join-Path $Destination 'app\whisper.cpp\build-vulkan\bin\Release'
    New-Item -ItemType Directory -Force -Path $cpuDest | Out-Null
    New-Item -ItemType Directory -Force -Path $vulkanDest | Out-Null
    Copy-Item -Path (Join-Path $cpuDir '*') -Destination $cpuDest -Recurse -Force
    Copy-Item -Path (Join-Path $vulkanDir '*') -Destination $vulkanDest -Recurse -Force

    $ffmpegDest = Join-Path $Destination 'tools\ffmpeg\bin'
    New-Item -ItemType Directory -Force -Path $ffmpegDest | Out-Null
    Copy-Item -Path (Join-Path $ffmpegExe.Directory.FullName '*') -Destination $ffmpegDest -Recurse -Force

    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $Destination -Force
    Set-Content -LiteralPath (Join-Path $Destination 'VERSION.txt') -Value '1.0.0' -Encoding UTF8

    $licenseDest = Join-Path $Destination 'third-party-licenses'
    New-Item -ItemType Directory -Force -Path $licenseDest | Out-Null

    $licenseCandidates = @(
        (Join-Path $root 'app\whisper.cpp\LICENSE'),
        (Join-Path $root 'app\whisper.cpp\LICENSE.md')
    )
    foreach ($candidate in $licenseCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Copy-Item -LiteralPath $candidate -Destination (Join-Path $licenseDest ('whisper.cpp-' + (Split-Path -Leaf $candidate))) -Force
        }
    }

    $ffmpegRoot = Join-Path $root 'tools\ffmpeg'
    Get-ChildItem -LiteralPath $ffmpegRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)(\..*)?$' } |
        Select-Object -First 8 |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDest ('ffmpeg-' + $_.Name)) -Force
        }

    Write-Host 'Verifying release payload...' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'verify-release-payload.ps1') -PayloadDirectory $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Release payload verifier failed with exit code $LASTEXITCODE"
    }

    $size = (Get-ChildItem -LiteralPath $Destination -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ''
    Write-Host 'Release payload ready.' -ForegroundColor Green
    Write-Host "Path: $Destination"
    Write-Host ('Size: {0:N2} MB' -f ($size / 1MB))
}
finally {
    Remove-Item -LiteralPath $publishTemp -Recurse -Force -ErrorAction SilentlyContinue
}
