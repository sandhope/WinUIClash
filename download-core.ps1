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

.PARAMETER OutputRoot
    Output directory. Defaults to ../Core relative to this script.

.EXAMPLE
    .\scripts\download-core.ps1

.EXAMPLE
    .\scripts\download-core.ps1 -GitHubToken "ghp_xxx"

.EXAMPLE
    .\scripts\download-core.ps1 -Version v1.19.0
#>

param(
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$Repo = "MetaCubeX/mihomo",
    [string]$Version = "",
    [string]$OutputRoot = "$PSScriptRoot/WinUIClash/Core"
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

Write-Host "=== mihomo core downloader ===" -ForegroundColor Cyan

# 1. Resolve target version
if ($Version) {
    $tag = $Version
    Write-Host "Using specified version: $tag" -ForegroundColor Green
}
else {
    $releaseUrl = "https://api.github.com/repos/$Repo/releases/latest"
    Write-Host "Fetching $releaseUrl ..."
    try {
        $release = Get-GitHubApi $releaseUrl
    }
    catch {
        Write-Error "Failed to fetch release info: $_"
        exit 1
    }
    $tag = $release.tag_name
    Write-Host "Latest version: $tag" -ForegroundColor Green
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

# 2. Download per architecture
$archMap = @{
    "x64"   = @{ AssetKeyword = "windows-amd64"; BinaryName = "mihomo-windows-amd64.exe"; OutputName = "mihomo.exe" }
    "arm64" = @{ AssetKeyword = "windows-arm64"; BinaryName = "mihomo-windows-arm64.exe"; OutputName = "mihomo-arm64.exe" }
}

foreach ($arch in $archMap.Keys) {
    $info = $archMap[$arch]
    $outPath = Join-Path $OutputRoot $info.OutputName

    $downloadUrl = "https://github.com/$Repo/releases/download/$tag/mihomo-$($info.AssetKeyword)-$tag.zip"
    $zipPath = Join-Path $env:TEMP "mihomo-$arch.zip"

    Write-Host "  [$arch] Downloading $($info.AssetKeyword) ..." -ForegroundColor Yellow

    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing
    }
    catch {
        Write-Warning "  [$arch] Download failed: $_"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        continue
    }

    # 3. Extract → Core/
    Write-Host "  [$arch] Extracting to $outPath ..." -ForegroundColor Yellow
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.Name -eq $info.BinaryName } | Select-Object -First 1
        if ($entry) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $outPath, $true)
            Write-Host "    OK: $($info.OutputName)" -ForegroundColor Green
        }
        else {
            Write-Warning "  [$arch] $($info.BinaryName) not found in archive"
        }
    }
    finally {
        $zip.Dispose()
    }
    Remove-Item $zipPath -Force
}

# 4. Summary
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Get-ChildItem $OutputRoot -File | ForEach-Object {
    $s = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.FullName)  ($s MB)"
}
