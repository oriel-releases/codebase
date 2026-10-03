# Builds, packages and publishes a release to GitHub Releases. Installed copies of Oriel pick it up
# within a day (Settings > Updates > Check for updates to get it right away).
#
#   1. Bump <Version> in src\Oriel.App\Oriel.App.csproj
#   2. $env:GITHUB_TOKEN = '<token with Contents: read and write on akbarjorayev/oriel>'
#   3. .\scripts\release.ps1                (or -NoUpload to only build .\releases locally)
param(
    [ValidateSet('x64', 'arm64')] [string]$Arch = 'x64',
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$NoUpload,
    # Optional code signing (Azure Trusted Signing metadata.json); removes the SmartScreen warning over time.
    [string]$AzureSignFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repoUrl = 'https://github.com/akbarjorayev/oriel'
$packId = 'OrielApp'          # install folder %LOCALAPPDATA%\OrielApp; never change it, or updates break
$vpkVersion = '1.2.161'       # keep in step with the Velopack package in Oriel.App.csproj
$channel = if ($Arch -eq 'x64') { 'win' } else { "win-$Arch" }
$out = Join-Path $root 'releases'
$csproj = Join-Path $root 'src\Oriel.App\Oriel.App.csproj'

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $csproj" }
if (-not $NoUpload -and -not $Token) { throw 'Set $env:GITHUB_TOKEN (or pass -Token), or use -NoUpload' }
Write-Host "Releasing Oriel $version ($channel)"

if ((dotnet tool list -g) -notmatch "vpk\s+$([regex]::Escape($vpkVersion))") {
    dotnet tool update -g vpk --version $vpkVersion
    if ($LASTEXITCODE -ne 0) { throw 'Installing vpk failed' }
}
$vpk = Join-Path $env:USERPROFILE '.dotnet\tools\vpk.exe'   # global tools may not be on PATH yet

& (Join-Path $PSScriptRoot 'publish.ps1') -Arch $Arch

# The previous release lets vpk build a small delta update. The first release has none.
if (-not $NoUpload) {
    & $vpk download github --repoUrl $repoUrl --token $Token --channel $channel --outputDir $out
    if ($LASTEXITCODE -ne 0) { Write-Host 'No previous release found; making a full release only.' }
}

$pack = @('pack', '--packId', $packId, '--packTitle', 'Oriel', '--packAuthors', 'Akbarjon Jorayev',
    '--packVersion', $version, '--packDir', (Join-Path $root "artifacts\Oriel-win-$Arch"),
    '--mainExe', 'Oriel.exe', '--icon', (Join-Path $root 'src\Oriel.App\Assets\AppIcon.ico'),
    '--channel', $channel, '--shortcuts', 'StartMenuRoot', '--outputDir', $out)
if ($AzureSignFile) { $pack += @('--azureTrustedSignFile', $AzureSignFile) }
& $vpk @pack
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

if ($NoUpload) { Write-Host "Built $out (not uploaded)"; return }

$tag = "v$version"
& $vpk upload github --repoUrl $repoUrl --token $Token --channel $channel --outputDir $out `
    --publish --releaseName "Oriel $version" --tag $tag
if ($LASTEXITCODE -ne 0) { throw 'vpk upload failed' }

# Also attach the installer as Oriel-Setup.exe: the website links to
# $repoUrl/releases/latest/download/Oriel-Setup.exe, which always serves the newest one.
if ($Arch -eq 'x64') {
    $headers = @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json' }
    $api = $repoUrl -replace '^https://github.com/', 'https://api.github.com/repos/'
    $uploads = $repoUrl -replace '^https://github.com/', 'https://uploads.github.com/repos/'
    $release = Invoke-RestMethod "$api/releases/tags/$tag" -Headers $headers
    Invoke-RestMethod -Method Post -Headers $headers -ContentType 'application/octet-stream' `
        -Uri "$uploads/releases/$($release.id)/assets?name=Oriel-Setup.exe" `
        -InFile (Join-Path $out "$packId-$channel-Setup.exe") | Out-Null
}

Write-Host "Published $repoUrl/releases/tag/$tag"
Write-Host "Now update version and releasedOn in website\src\config\release.ts and redeploy the site."
