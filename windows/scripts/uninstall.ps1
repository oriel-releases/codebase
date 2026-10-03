# Removes Oriel. Pass -KeepData to keep settings, history and downloaded wallpapers.
param([switch]$KeepData)
$ErrorActionPreference = 'Continue'
Get-Process Oriel -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Oriel -ErrorAction SilentlyContinue
foreach ($task in 'Oriel\ApplyToday', 'Oriel\Retry') { schtasks /Delete /TN $task /F 2>$null | Out-Null }
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Oriel.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\Oriel') -Recurse -Force -ErrorAction SilentlyContinue
if (-not $KeepData) {
    Remove-Item (Join-Path $env:LOCALAPPDATA 'Oriel') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Settings and history removed. Wallpapers in Pictures\Oriel were left alone."
}
Write-Host "Oriel uninstalled."
