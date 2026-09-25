$processes = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('whisper-cli.exe', 'ffmpeg.exe') -or
    ($_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*transcribe.ps1*')
} | Select-Object Name, ProcessId, ParentProcessId, CommandLine

if ($processes) {
    Write-Host 'WhisperMd-related processes are still running:' -ForegroundColor Yellow
    $processes | Format-Table -AutoSize
    exit 1
}

Write-Host 'PASS: no whisper-cli / ffmpeg / transcribe.ps1 processes found.' -ForegroundColor Green
exit 0
