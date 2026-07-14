#Requires -Version 5.1
<#
.SYNOPSIS
    CI/CD script: download mihomo binaries from GitHub Releases into Core/ directory.

.DESCRIPTION
    Fetches the latest stable mihomo release, downloads windows-amd64 and windows-arm64
    ZIP archives, extracts mihomo.exe, and places them in:
        Core/   (for x86/x64, as mihomo.exe)
        Core/   (for ARM64, as mihomo-arm64.exe)

    The csproj automatically links these files into the build output when they exist.
    When files are missing (e.g. after git clone), build silently skips them and the app
    falls back to runtime download.

.PARAMETER GitHubToken
    GitHub PAT to bypass API rate limiting (60 req/hr anonymous). Also reads GITHUB_TOKEN env var.

.PARAMETER Version
    Optional specific mihomo version tag (e.g. v1.19.0). If omitted, fetches the latest release.

.PARAMETER ExpectedSha256
    Optional hashtable mapping arch to expected SHA256 of the downloaded ZIP.
    e.g. @{ x64 = "abc123..."; arm64 = "def456..." }

.PARAMETER OutputRoot
    Output directory. Defaults to ../Core relative to this script.

.PARAMETER Force
    Re-download even if the binary already exists.

.EXAMPLE
    .\scripts\download-core.ps1

.EXAMPLE
    .\scripts\download-core.ps1 -GitHubToken "ghp_xxx"

.EXAMPLE
    .\scripts\download-core.ps1 -Version v1.19.0

.EXAMPLE
    .\scripts\download-core.ps1 -Force
#>

param(
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$Repo = "MetaCubeX/mihomo",
    [string]$Version = "",
    [hashtable]$ExpectedSha256 = @{},
    [string]$OutputRoot = "$PSScriptRoot/WinUIClash/Core",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# PS 5.1 needs explicit assembly load for ZipFile
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Ensure TLS 1.2+ on older PowerShell / Windows
[System.Net.ServicePointManager]::SecurityProtocol =
    [System.Net.SecurityProtocolType]::Tls12 -bor
    [System.Net.SecurityProtocolType]::Tls13

# ── HTTP helper ──
function Get-GitHubApi($Url) {
    $headers = @{ "User-Agent" = "WinUIClash-CI/1.0" }
    if ($GitHubToken) {
        $headers["Authorization"] = "Bearer $GitHubToken"
    }
    return Invoke-RestMethod -Uri $Url -Headers $headers
}

# ── Asset search with regex fallback ──
function Find-ReleaseAsset($Release, [string[]]$Patterns) {
    foreach ($pattern in $Patterns) {
        $asset = $Release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
        if ($asset) { return $asset }
    }
    return $null
}

# ── GPL compliance: LICENSE & NOTICE files ──
function Ensure-DistributionFiles {
    param(
        [string]$VersionText,
        [string]$AssetName,
        [string]$AssetUrl,
        [string]$ReleaseUrl
    )

    $licensePath = Join-Path $OutputRoot "mihomo-LICENSE.txt"
    $noticePath  = Join-Path $OutputRoot "mihomo-NOTICE.txt"

    if (-not (Test-Path $licensePath)) {
        try {
            Invoke-WebRequest -Uri "https://www.gnu.org/licenses/gpl-3.0.txt" -OutFile $licensePath -UseBasicParsing
        }
        catch { Write-Warning "Failed to download GPL license: $_" }
    }

    $notice = @"
Bundled component: mihomo core
Bundled version: $VersionText

Upstream project: MetaCubeX/mihomo
Upstream release: $ReleaseUrl
Upstream asset: $AssetName
Upstream asset URL: $AssetUrl
Upstream documentation: https://wiki.metacubex.one/

License: GPL-3.0. See mihomo-LICENSE.txt in this directory.

Source availability: the upstream release page publishes the corresponding release,
source archive links, and source-related assets. WinUIClash redistributes the
unmodified Windows mihomo core as a bundled runtime dependency.

Trademark/naming note: WinUIClash is not affiliated with MetaCubeX and does not
use "mihomo" in the application name.
"@
    Set-Content -LiteralPath $noticePath -Value $notice -Encoding UTF8
}

Write-Host "=== mihomo core downloader ===" -ForegroundColor Cyan

# 1. Resolve target version & fetch release info
$releaseUrl = if ($Version) {
    "https://api.github.com/repos/$Repo/releases/tags/$Version"
} else {
    "https://api.github.com/repos/$Repo/releases/latest"
}
Write-Host "Fetching $releaseUrl ..."
try {
    $release = Get-GitHubApi $releaseUrl
}
catch {
    Write-Error "Failed to fetch release info: $_"
    exit 1
}
$tag = $release.tag_name
Write-Host "Target version: $tag" -ForegroundColor Green

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

# 2. Download per architecture
$archMap = [ordered]@{
    "x64"   = @{
        AssetPatterns = @(
            "^mihomo-windows-amd64-compatible-.*\.zip$"
            "^mihomo-windows-amd64-v1-v.*\.zip$"
            "^mihomo-windows-amd64-.*\.zip$"
        )
        OutputName = "mihomo.exe"
    }
    "arm64" = @{
        AssetPatterns = @(
            "^mihomo-windows-arm64-.*\.zip$"
        )
        OutputName = "mihomo-arm64.exe"
    }
}

foreach ($arch in $archMap.Keys) {
    $info = $archMap[$arch]
    $outPath = Join-Path $OutputRoot $info.OutputName

    # Skip if binary already exists and -Force not set
    if ((Test-Path $outPath) -and -not $Force) {
        $versionText = & $outPath -v 2>$null | Select-Object -First 1
        Write-Host "  [$arch] Already exists: $versionText (use -Force to re-download)" -ForegroundColor DarkGray
        continue
    }

    # Find matching asset from release
    $asset = Find-ReleaseAsset $release $info.AssetPatterns
    if ($null -eq $asset) {
        Write-Warning "  [$arch] No matching asset found in release $tag"
        continue
    }

    $downloadUrl = $asset.browser_download_url
    $zipPath = Join-Path $env:TEMP $asset.name
    $extractDir = Join-Path $env:TEMP "mihomo-extract-$arch"

    Write-Host "  [$arch] Downloading $($asset.name) ..." -ForegroundColor Yellow

    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing
    }
    catch {
        Write-Warning "  [$arch] Download failed: $_"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        continue
    }

    # 3. SHA256 verification
    if ($ExpectedSha256.ContainsKey($arch)) {
        $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        if (-not $actualHash.Equals($ExpectedSha256[$arch], [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Error "  [$arch] SHA256 mismatch! Expected $($ExpectedSha256[$arch]), got $actualHash"
            Remove-Item $zipPath -Force
            continue
        }
        Write-Host "    SHA256 verified OK" -ForegroundColor Green
    }

    # 4. Extract → Core/
    Write-Host "  [$arch] Extracting to $outPath ..." -ForegroundColor Yellow
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

    $binary = Get-ChildItem -Path $extractDir -Recurse -File | Where-Object { $_.Extension -eq ".exe" } | Select-Object -First 1
    if ($null -eq $binary) {
        Write-Warning "  [$arch] No .exe found in archive"
        continue
    }

    Copy-Item -LiteralPath $binary.FullName -Destination $outPath -Force

    Write-Host "    OK: $($info.OutputName) ($($asset.name))" -ForegroundColor Green

    # Cleanup
    Remove-Item $zipPath -Force
    Remove-Item $extractDir -Recurse -Force
}

# 5. GPL compliance files
$primaryExe = Join-Path $OutputRoot "mihomo.exe"
if (Test-Path $primaryExe) {
    $versionText = & $primaryExe -v 2>$null | Select-Object -First 1
    $primaryAsset = $null
    foreach ($arch in $archMap.Keys) {
        $a = Find-ReleaseAsset $release $archMap[$arch].AssetPatterns
        if ($a) { $primaryAsset = $a; break }
    }
    Ensure-DistributionFiles `
        -VersionText $versionText `
        -AssetName $(if ($primaryAsset) { $primaryAsset.name } else { "" }) `
        -AssetUrl $(if ($primaryAsset) { $primaryAsset.browser_download_url } else { "" }) `
        -ReleaseUrl $release.html_url
    Write-Host "  Generated LICENSE & NOTICE files" -ForegroundColor DarkGray
}

# 6. Summary
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Get-ChildItem $OutputRoot -File | ForEach-Object {
    $s = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.FullName)  ($s MB)"
}
