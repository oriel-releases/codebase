# Per-user install (no admin): copies the published app to %LOCALAPPDATA%\Programs\Oriel,
# adds a Start menu shortcut and starts it. Run scripts\publish.ps1 first.
param([ValidateSet('x64', 'arm64')] [string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root "artifacts\Oriel-win-$Arch"
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Oriel'
if (-not (Test-Path (Join-Path $src 'Oriel.exe'))) { throw "Run scripts\publish.ps1 first ($src not found)" }

Get-Process Oriel -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item $src $dest -Recurse

$exe = Join-Path $dest 'Oriel.exe'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Oriel.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'Daily wallpaper from Unsplash'
$lnk.Save()

# First launch re-points the Run key and the scheduled task at the installed exe.
Start-Process $exe
Write-Host "Installed to $dest"
