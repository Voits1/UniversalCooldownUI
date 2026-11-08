# Builds the client plugin and packs the Thunderstore package into dist/.
#
# Usage:
#   .\build-package.ps1 -BepInExRoot "C:\path\to\profile\BepInEx"
#
# Result: dist/UniversalCooldownUI-<version>.zip containing
#   manifest.json, README.md, icon.png, plugins/UniversalCooldownUI.dll

param(
    [Parameter(Mandatory = $false)]
    [string]$BepInExRoot = "$env:USERPROFILE\AppData\Roaming\r2modmanPlus-local\VRising\profiles\Default\BepInEx",

    [string]$Configuration = "Release",

    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "BepInEx: $BepInExRoot"

$project = Join-Path $root "client\UniversalCooldownUI\UniversalCooldownUI.csproj"
& $Dotnet build $project -c $Configuration "-p:BepInExRoot=$BepInExRoot"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

$dll = Join-Path $root "client\UniversalCooldownUI\bin\$Configuration\UniversalCooldownUI.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

$manifest = Get-Content (Join-Path $root "thunderstore\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number

$stage = Join-Path $root "dist\package"
if (Test-Path (Join-Path $root "dist")) { Remove-Item (Join-Path $root "dist") -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage "plugins") | Out-Null

Copy-Item (Join-Path $root "thunderstore\manifest.json") $stage -Force
Copy-Item (Join-Path $root "thunderstore\README.md") $stage -Force
Copy-Item (Join-Path $root "thunderstore\icon.png") $stage -Force
Copy-Item $dll (Join-Path $stage "plugins\UniversalCooldownUI.dll") -Force

$zip = Join-Path $root "dist\UniversalCooldownUI-$version.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force

Write-Host "Package: $zip"
