[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$InputPath,

    [ValidateSet('auto', 'vulkan', 'cpu')]
    [string]$Backend = 'auto',

    [string]$Language = 'ru',

    [ValidateRange(1, 32)]
    [int]$Threads = 8,

    [string]$Prompt = '',

    [string]$WorkingDirectory = '',

    [string]$OutputDirectory = '',

    [switch]$KeepWav
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ProtocolVersion = 1
$script:EventPrefix = 'WHISPERMD_EVENT '

# The WPF backend client reads protocol events as UTF-8 JSON lines from stdout.
# Keep human/diagnostic output separate on stderr where possible.
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$OutputEncoding = $utf8

function Write-WhisperMdEvent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Type,

        [hashtable]$Data = @{}
    )

    $payload = [ordered]@{
        version = $script:ProtocolVersion
        type = $Type
        timestamp = [DateTimeOffset]::UtcNow.ToString('o')
    }

    foreach ($key in $Data.Keys) {
        $payload[$key] = $Data[$key]
    }

    $json = $payload | ConvertTo-Json -Compress -Depth 8
    [Console]::Out.WriteLine($script:EventPrefix + $json)
    [Console]::Out.Flush()
}

function Write-TechLog {
    param(
        [AllowEmptyString()]
        [string]$Message
    )

    [Console]::Error.WriteLine($Message)
    [Console]::Error.Flush()
}

function Invoke-LoggedNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$ParseWhisperProgress
    )

    # Windows PowerShell 5.1 wraps native stderr as error records. Keep those
    # diagnostic lines flowing through the parser without terminating on them.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Write-TechLog $line

            if ($ParseWhisperProgress -and $line -match 'progress\s*=\s*(\d{1,3})%') {
                $progress = [Math]::Max(0, [Math]::Min(100, [int]$Matches[1]))
                Write-WhisperMdEvent -Type 'progress' -Data @{ value = $progress }
            }
        }

        $exitCode = [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return $exitCode
}

try {
    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
        throw "Input file not found: $InputPath"
    }

    $root = $PSScriptRoot
    $resolvedInput = (Resolve-Path -LiteralPath $InputPath).Path

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

    $workingDir = if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        Join-Path $root 'working'
    }
    else {
        [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($WorkingDirectory.Trim()))
    }

    $outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        Join-Path $root 'output'
    }
    else {
        [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($OutputDirectory.Trim()))
    }

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

    Write-WhisperMdEvent -Type 'stage' -Data @{ stage = 'preparing' }
    Write-TechLog "Preparing audio: $resolvedInput"

    $ffmpegArguments = @(
        '-hide_banner',
        '-loglevel', 'error',
        '-y',
        '-i', $resolvedInput,
        '-ar', '16000',
        '-ac', '1',
        '-c:a', 'pcm_s16le',
        $wavPath
    )

    $ffmpegExitCode = Invoke-LoggedNativeCommand -FilePath $ffmpeg.FullName -Arguments $ffmpegArguments
    if ($ffmpegExitCode -ne 0) {
        throw "FFmpeg failed with exit code $ffmpegExitCode. The source recording was preserved."
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

        if (-not [string]::IsNullOrWhiteSpace($Prompt)) {
            $arguments += @('--prompt', $Prompt.Trim())
        }

        if ($BackendName -eq 'cpu') {
            $arguments += '-ng'
        }

        Write-WhisperMdEvent -Type 'backend_selected' -Data @{ backend = $BackendName }
        Write-TechLog "Starting Whisper ($BackendName, language: $Language, threads: $Threads)..."
        Write-WhisperMdEvent -Type 'progress' -Data @{ value = 0 }

        return Invoke-LoggedNativeCommand -FilePath $CliPath -Arguments $arguments -ParseWhisperProgress
    }

    $selectedBackend = $Backend
    if ($selectedBackend -eq 'auto') {
        $selectedBackend = if (Test-Path -LiteralPath $vulkanCli -PathType Leaf) { 'vulkan' } else { 'cpu' }
    }

    Write-WhisperMdEvent -Type 'stage' -Data @{ stage = 'transcribing' }

    $exitCode = if ($selectedBackend -eq 'vulkan') {
        if (-not (Test-Path -LiteralPath $vulkanCli -PathType Leaf)) {
            throw "Vulkan build not found: $vulkanCli"
        }

        Invoke-Whisper -CliPath $vulkanCli -BackendName 'vulkan'
    }
    else {
        Invoke-Whisper -CliPath $cpuCli -BackendName 'cpu'
    }

    if ($exitCode -ne 0 -and $Backend -eq 'auto' -and $selectedBackend -eq 'vulkan') {
        $message = "Vulkan failed with exit code $exitCode. Retrying on the CPU."
        Write-WhisperMdEvent -Type 'warning' -Data @{ message = $message }
        Write-WhisperMdEvent -Type 'fallback' -Data @{
            fromBackend = 'vulkan'
            toBackend = 'cpu'
            exitCode = $exitCode
        }
        Write-TechLog $message

        $exitCode = Invoke-Whisper -CliPath $cpuCli -BackendName 'cpu'
        $selectedBackend = 'cpu'
    }

    if ($exitCode -ne 0) {
        throw "Whisper failed with exit code $exitCode. The source recording and temporary WAV were preserved."
    }

    Write-WhisperMdEvent -Type 'progress' -Data @{ value = 100 }
    Write-WhisperMdEvent -Type 'stage' -Data @{ stage = 'validating' }

    $txtPath = "$outputPrefix.txt"
    $srtPath = "$outputPrefix.srt"
    $jsonPath = "$outputPrefix.json"
    $expectedOutputs = @($txtPath, $srtPath, $jsonPath)

    $missingOutputs = $expectedOutputs | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
    if ($missingOutputs) {
        throw "Transcription completed without the expected files: $($missingOutputs -join ', ')"
    }

    if (-not $KeepWav) {
        Remove-Item -LiteralPath $wavPath -Force
    }

    Write-WhisperMdEvent -Type 'result' -Data @{
        backend = $selectedBackend
        resultDirectory = $resultDir
        txtPath = $txtPath
        srtPath = $srtPath
        jsonPath = $jsonPath
    }
    Write-WhisperMdEvent -Type 'stage' -Data @{ stage = 'completed' }

    # Legacy human-readable summary retained temporarily for the old WinForms GUI.
    [Console]::Out.WriteLine('')
    [Console]::Out.WriteLine("Done. Backend: $selectedBackend")
    [Console]::Out.WriteLine("Results: $resultDir")
    [Console]::Out.WriteLine('The source recording was not deleted.')
    [Console]::Out.Flush()
}
catch {
    $message = $_.Exception.Message
    Write-WhisperMdEvent -Type 'error' -Data @{ message = $message }
    Write-TechLog "ERROR: $message"
    exit 1
}
