[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [string]$Prompt = 'WhisperMd protocol verification: mathematics lecture, numerical series.'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$transcribe = Join-Path $root 'transcribe.ps1'
$eventPrefix = 'WHISPERMD_EVENT '

if (-not (Test-Path -LiteralPath $transcribe -PathType Leaf)) {
    throw "transcribe.ps1 not found: $transcribe"
}

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    throw "Input file not found: $InputPath"
}

$resolvedInput = (Resolve-Path -LiteralPath $InputPath).Path

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-ProtocolCheck {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('cpu', 'vulkan')]
        [string]$BackendName
    )

    Write-Host ""
    Write-Host "=== Backend: $BackendName ==="

    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("whispermd-protocol-{0}-{1}.stderr.log" -f $BackendName, [Guid]::NewGuid().ToString('N'))
    $passed = $false

    try {
        $arguments = @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $transcribe,
            '-InputPath', $resolvedInput,
            '-Backend', $BackendName,
            '-Language', 'ru',
            '-Threads', '8',
            '-Prompt', $Prompt
        )

        # Windows PowerShell 5.1 can promote redirected native stderr to a
        # terminating NativeCommandError while ErrorActionPreference is Stop.
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $stdoutLines = @(& powershell.exe @arguments 2> $stderrPath)
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $exitCode = $LASTEXITCODE

        Assert-Condition ($exitCode -eq 0) "transcribe.ps1 failed on $BackendName with exit code $exitCode. See: $stderrPath"

        $events = @()
        $legacyResultLineFound = $false

        foreach ($item in $stdoutLines) {
            $line = [string]$item

            if ($line.StartsWith('Results:', [StringComparison]::OrdinalIgnoreCase)) {
                $legacyResultLineFound = $true
            }

            if (-not $line.StartsWith($eventPrefix, [StringComparison]::Ordinal)) {
                continue
            }

            $json = $line.Substring($eventPrefix.Length)
            try {
                $events += ($json | ConvertFrom-Json)
            }
            catch {
                throw "Invalid protocol JSON on ${BackendName}: $line"
            }
        }

        Assert-Condition ($events.Count -gt 0) "No protocol events were emitted on $BackendName."
        Assert-Condition (-not (@($events | Where-Object { $_.version -ne 1 }).Count -gt 0)) "Unexpected protocol version on $BackendName."

        $types = @($events | ForEach-Object { [string]$_.type })
        foreach ($requiredType in @('stage', 'backend_selected', 'progress', 'result')) {
            Assert-Condition ($types -contains $requiredType) "Missing '$requiredType' event on $BackendName."
        }

        $progressValues = @($events | Where-Object { $_.type -eq 'progress' } | ForEach-Object { [double]$_.value })
        Assert-Condition ($progressValues.Count -gt 0) "No progress values on $BackendName."
        Assert-Condition (-not (@($progressValues | Where-Object { $_ -lt 0 -or $_ -gt 100 }).Count -gt 0)) "Progress outside 0..100 on $BackendName."

        $result = @($events | Where-Object { $_.type -eq 'result' }) | Select-Object -Last 1
        Assert-Condition ($null -ne $result) "Missing result event on $BackendName."
        Assert-Condition ([string]$result.backend -eq $BackendName) "Result backend '$($result.backend)' does not match requested '$BackendName'."

        foreach ($property in @('resultDirectory', 'txtPath', 'srtPath', 'jsonPath')) {
            $path = [string]$result.$property
            Assert-Condition (-not [string]::IsNullOrWhiteSpace($path)) "Result event has empty '$property' on $BackendName."
            Assert-Condition (Test-Path -LiteralPath $path) "Result path '$property' does not exist on ${BackendName}: $path"
        }

        Assert-Condition $legacyResultLineFound "Legacy Results: line is missing on $BackendName."

        $stderrLength = if (Test-Path -LiteralPath $stderrPath) { (Get-Item -LiteralPath $stderrPath).Length } else { 0 }

        Write-Host "PASS $BackendName"
        Write-Host "  events: $($events.Count)"
        Write-Host "  progress: $($progressValues -join ', ')"
        Write-Host "  result: $($result.resultDirectory)"
        Write-Host "  technical stderr bytes: $stderrLength"
        Write-Host "  legacy Results: preserved"
        $passed = $true
    }
    catch {
        Write-Host "FAIL $BackendName" -ForegroundColor Red
        if (Test-Path -LiteralPath $stderrPath) {
            Write-Host "Technical stderr: $stderrPath"
        }
        throw
    }
    finally {
        if ($passed -and (Test-Path -LiteralPath $stderrPath)) {
            Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Invoke-ProtocolCheck -BackendName 'cpu'
Invoke-ProtocolCheck -BackendName 'vulkan'

Write-Host ""
Write-Host 'Backend protocol verification passed for CPU and Vulkan.'
