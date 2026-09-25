[CmdletBinding()]
param(
    [string]$Destination = "C:\Base\work_archive\WhisperMd-source-current.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("WhisperMd-source-" + [Guid]::NewGuid().ToString('N'))

$rootExcludedDirectories = @('app','models','tools','inbox','working','output','archive','releases','release','.git','.vs')
$excludedDirectoryNames = @('bin','obj')
$mediaExtensions = @('.wav','.mp3','.m4a','.aac','.ogg','.opus','.flac','.wma','.mp4','.mkv','.webm','.mov')

try {
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($root.Length).TrimStart('\','/')
        $segments = $relative -split '[\\/]'

        if ($segments.Count -gt 0 -and $rootExcludedDirectories -contains $segments[0]) { return }
        if ($segments | Where-Object { $excludedDirectoryNames -contains $_ }) { return }
        if ($mediaExtensions -contains $_.Extension.ToLowerInvariant()) { return }

        $target = Join-Path $temp $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $temp '*') -DestinationPath $Destination -CompressionLevel Optimal

    $file = Get-Item -LiteralPath $Destination
    Write-Host "Created: $($file.FullName)" -ForegroundColor Green
    Write-Host ("Size: {0:N2} MB" -f ($file.Length / 1MB))
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
