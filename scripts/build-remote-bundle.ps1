#!/usr/bin/env pwsh
# Builds Release SizerDataCollector.Service and lays out a CollectorAgent remote-install bundle under dist/.
# Usage (from repo root):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-remote-bundle.ps1
#   powershell ... -File scripts\build-remote-bundle.ps1 -BundleVersion 9 -Zip

param(
    [int]$BundleVersion = 9,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$serviceProj = Join-Path $repoRoot "SizerDataCollector.Service\SizerDataCollector.Service.csproj"
$releaseDir = Join-Path $repoRoot "SizerDataCollector.Service\bin\Release"
$bundleName = "CollectorAgent-remote-v$BundleVersion"
$bundleRoot = Join-Path $repoRoot "dist\$bundleName"
$serviceDest = Join-Path $bundleRoot "service"

if (-not (Test-Path $serviceProj)) {
    throw "Service project not found: $serviceProj"
}

$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    throw "MSBuild.exe not found. Install Visual Studio 2022 build tools or run from a Developer PowerShell."
}

Write-Host "==> Building Release: $serviceProj"
& $msbuild $serviceProj /restore /t:Rebuild /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path (Join-Path $releaseDir "SizerDataCollector.Service.exe"))) {
    throw "Release output missing. Expected: $(Join-Path $releaseDir 'SizerDataCollector.Service.exe')"
}

Write-Host "==> Staging bundle: $bundleRoot"
if (Test-Path $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $serviceDest -Force | Out-Null

Get-ChildItem -LiteralPath $releaseDir -Force | Where-Object { $_.Name -ne "logs" } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $serviceDest -Recurse -Force
}

$installScript = Join-Path $repoRoot "scripts\install-from-bundle.ps1"
$readme = Join-Path $repoRoot "scripts\REMOTE_BUNDLE_README.md"
Copy-Item -LiteralPath $installScript -Destination (Join-Path $bundleRoot "install-from-bundle.ps1") -Force
Copy-Item -LiteralPath $readme -Destination (Join-Path $bundleRoot "REMOTE_BUNDLE_README.md") -Force

Write-Host "==> Bundle ready: $bundleRoot"
Write-Host "    Service EXE: $(Join-Path $serviceDest 'SizerDataCollector.Service.exe')"

if ($Zip) {
    $zipPath = Join-Path $repoRoot "dist\$bundleName.zip"
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    # Copy to temp before zipping — avoids Compress-Archive failing when IDE/antivirus locks files under dist\.
    $zipStageParent = Join-Path ([System.IO.Path]::GetTempPath()) ("CollectorAgent-remote-staging-" + [Guid]::NewGuid().ToString("N"))
    $zipStageRoot = Join-Path $zipStageParent $bundleName
    try {
        New-Item -ItemType Directory -Path $zipStageParent -Force | Out-Null
        Copy-Item -LiteralPath $bundleRoot -Destination $zipStageRoot -Recurse -Force
        Compress-Archive -LiteralPath $zipStageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    }
    finally {
        Remove-Item -LiteralPath $zipStageParent -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "==> Zip: $zipPath"
}
