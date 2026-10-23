param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$DownloadUrl,
    [string]$RepoUrl = "",
    [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $root "bin\Release\SGTProductionBox.json"
}

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

$manifestText = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.Encoding]::UTF8)
$manifest = $manifestText | ConvertFrom-Json

if (-not $RepoUrl) {
    $RepoUrl = $manifest.RepoUrl
}

$lastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$tags = ($manifest.Tags | ForEach-Object { '"' + $_ + '"' }) -join ", "

$repoJson = @"
[
  {
    "Author": "SGT",
    "Name": "SGT Production Box",
    "InternalName": "SGTProductionBox",
    "AssemblyVersion": "$Version",
    "Description": "$($manifest.Description)",
    "ApplicableVersion": "any",
    "RepoUrl": "$RepoUrl",
    "Tags": [$tags],
    "CategoryTags": ["utility"],
    "LoadPriority": 0,
    "IsHide": false,
    "IsTestingExclusive": false,
    "DownloadLink": "$DownloadUrl",
    "DownloadLinkInstall": "$DownloadUrl",
    "DownloadLinkTesting": "$DownloadUrl",
    "LastUpdate": $lastUpdate,
    "DalamudApiLevel": 15,
    "LoadRequiredState": 0,
    "LoadSync": false,
    "CanUnloadAsync": false,
    "Punchline": "$($manifest.Punchline)",
    "AcceptsFeedback": true
  }
]
"@

$repoPath = Join-Path $root "repo.json"
[System.IO.File]::WriteAllText($repoPath, $repoJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "repo.json updated: $repoPath"
