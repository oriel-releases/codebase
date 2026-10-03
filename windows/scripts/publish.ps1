# Builds a self-contained Release copy of Oriel into .\artifacts\Oriel-<rid>
param([ValidateSet('x64', 'arm64')] [string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts\Oriel-win-$Arch"

dotnet publish (Join-Path $root 'src\Oriel.App\Oriel.App.csproj') `
    -c Release -r "win-$Arch" -p:Platform=$Arch --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Write-Host "Published to $out"
