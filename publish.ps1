param(
    [string]$Owner = "your-name",
    [string]$Repo = "SGTProductionBox",
    [string]$Version = "",
    [string]$DalamudHome = "",
    [string]$Mirror = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "project root: $root"
$csproj = Join-Path $root "SGTProductionBox.csproj"

if (-not $DalamudHome) {
    $candidates = @(
        (Join-Path $env:APPDATA "XIVLauncherCN\addon\Hooks\dev"),
        (Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $DalamudHome = $candidate
            break
        }
    }
}

if (-not $DalamudHome -or -not (Test-Path $DalamudHome)) {
    throw "Dalamud dev directory not found. Pass -DalamudHome."
}

$env:DALAMUD_HOME = $DalamudHome
dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

$manifestPath = Join-Path $root "bin\Release\SGTProductionBox.json"
$manifestText = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
$manifest = $manifestText | ConvertFrom-Json

if (-not $Version) {
    $Version = $manifest.AssemblyVersion
}

if (-not $Version) {
    throw "Version not found."
}

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zipName = "SGTProductionBox-v$Version.zip"
$zipPath = Join-Path $dist $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# DalamudPackager creates the ready-to-distribute zip in bin\Release\<AssemblyName>\latest.zip
$packaged = Join-Path $root "bin\Release\SGTProductionBox\latest.zip"
Write-Host "packaged zip: $packaged"
if (-not (Test-Path $packaged)) {
    throw "Packaged zip not found at $packaged"
}

Copy-Item $packaged $zipPath -Force

$url = "https://github.com/$Owner/$Repo/releases/download/v$Version/$zipName"
if ($Mirror) {
    # 国内直连镜像，例如 https://ghfast.top/
    $url = $Mirror + $url
}

& (Join-Path $root "update-repo.ps1") -Version $Version -DownloadUrl $url -RepoUrl "https://github.com/$Owner/$Repo"

Write-Host ""
Write-Host "zip       : $zipPath"
Write-Host "repo.json : $(Join-Path $root 'repo.json')"
Write-Host "download  : $url"
Write-Host ""
Write-Host "Next: upload the zip to GitHub Release v$Version, commit repo.json."
