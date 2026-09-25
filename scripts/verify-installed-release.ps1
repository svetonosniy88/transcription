[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,

    [string]$InputPath = '',

    [switch]$SkipBackendSmoke
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$install = [System.IO.Path]::GetFullPath($InstallDirectory)
& (Join-Path $PSScriptRoot 'verify-release-payload.ps1') -PayloadDirectory $install
# verify-release-payload.ps1 is a PowerShell script: it throws on failure and
# does not set LASTEXITCODE when no native process has run in this session.

if (-not $SkipBackendSmoke) {
    if ([string]::IsNullOrWhiteSpace($InputPath) -or -not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
        throw 'Provide -InputPath with a short real recording, or use -SkipBackendSmoke.'
    }

    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('WhisperMd-installed-smoke-' + [Guid]::NewGuid().ToString('N'))
    $working = Join-Path $smokeRoot 'working'
    $output = Join-Path $smokeRoot 'output'
    New-Item -ItemType Directory -Force -Path $working, $output | Out-Null

    try {
        foreach ($backend in @('cpu', 'vulkan')) {
            Write-Host "Installed backend smoke: $backend" -ForegroundColor Cyan
            $script = Join-Path $install 'transcribe.ps1'
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                # Windows PowerShell 5.1 can wrap native stderr from the child PowerShell
                # as error records. Capture those lines without terminating this verifier.
                $ErrorActionPreference = 'Continue'
                $captured = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script `
                    -InputPath $InputPath `
                    -Backend $backend `
                    -Language ru `
                    -Threads 8 `
                    -WorkingDirectory $working `
                    -OutputDirectory $output 2>&1
                $exitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            if ($exitCode -ne 0) {
                throw "Installed $backend smoke failed with exit code $exitCode.`n$($captured -join [Environment]::NewLine)"
            }

            $resultLine = $captured | Where-Object { $_.ToString().StartsWith('WHISPERMD_EVENT ') -and $_.ToString().Contains('"type":"result"') } | Select-Object -Last 1
            if (-not $resultLine) {
                throw "Installed $backend smoke produced no result event."
            }

            $json = $resultLine.ToString().Substring('WHISPERMD_EVENT '.Length) | ConvertFrom-Json
            foreach ($path in @($json.txtPath, $json.srtPath, $json.jsonPath)) {
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    throw "Installed $backend smoke output missing: $path"
                }
                if ((Get-Item -LiteralPath $path).Length -le 0) {
                    throw "Installed $backend smoke output is empty: $path"
                }
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host 'PASS: installed release payload' -ForegroundColor Green
if (-not $SkipBackendSmoke) {
    Write-Host 'PASS: installed CPU + Vulkan backend smoke' -ForegroundColor Green
}
