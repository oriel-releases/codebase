# Builds the signed release APK and publishes it to GitHub Releases (oriel-releases/android). Installed copies of
# Oriel pick it up the next time they open or change the wallpaper (Settings > Version checks right away).
#
#   1. .\scripts\release.ps1 -Version 1.0.1  (sets oriel.version in gradle.properties; versionCode follows)
#      $env:GITHUB_TOKEN = '<token with Contents: read and write on oriel-releases/android>'
#   2. Or -NoUpload to only build releases\Oriel.apk locally.
#
# Every release must be signed with the SAME key (android\keystore.properties + the .jks it points to, both
# git-ignored): Android refuses an update signed differently, so losing the key means users must reinstall.
param(
    [string]$Version,
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$NoUpload
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'oriel-releases/android'
$props = Join-Path $root 'gradle.properties'

if (-not (Test-Path (Join-Path $root 'keystore.properties'))) {
    throw 'No keystore.properties: release builds must be signed with the release key (see the comment at the top).'
}
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, not $Version" }
    (Get-Content $props) -replace '^oriel\.version=.*$', "oriel.version=$Version" | Set-Content $props -Encoding utf8
}
$Version = ((Get-Content $props) -match '^oriel\.version=' | Select-Object -First 1) -replace '^oriel\.version=', ''
if (-not $NoUpload -and -not $Token) { throw 'Set $env:GITHUB_TOKEN (or pass -Token), or use -NoUpload' }
Write-Host "Releasing Oriel for Android $Version"

if (-not $env:JAVA_HOME) { $env:JAVA_HOME = Join-Path $HOME '.jdks\ms-17.0.20.1' }
Push-Location $root
try {
    .\gradlew.bat assembleRelease
    if ($LASTEXITCODE -ne 0) { throw 'gradle assembleRelease failed' }
    .\gradlew.bat --stop | Out-Null
} finally {
    Pop-Location
}

$out = Join-Path $root 'releases'
New-Item -ItemType Directory -Force $out | Out-Null
$apk = Join-Path $out 'Oriel.apk'
Copy-Item (Join-Path $root 'app\build\outputs\apk\release\app-release.apk') $apk -Force
if ($NoUpload) { Write-Host "Built $apk (not uploaded)"; return }

# The app reads releases/latest and installs its .apk asset. The website can link to
# https://github.com/$repo/releases/latest/download/Oriel.apk, which always serves the newest one.
$headers = @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json' }
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers `
    -Body (@{ tag_name = "v$Version"; name = "Oriel $Version"; make_latest = 'true' } | ConvertTo-Json) -ContentType 'application/json'
$upload = $release.upload_url -replace '\{.*\}$', ''
Invoke-RestMethod -Method Post -Uri "${upload}?name=Oriel.apk" -Headers $headers -InFile $apk `
    -ContentType 'application/vnd.android.package-archive' | Out-Null
Write-Host "Published $($release.html_url)"
