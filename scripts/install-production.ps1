param(
    [string]$Configuration = "Release",
    [string]$InstallRoot = "C:\Program Files\Opti-Fresh\SizerDataCollector",
    [string]$ServiceName = "SizerDataCollectorService",
    [switch]$SkipBuild,
    [switch]$SkipPreflight,
    [switch]$SkipStart
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        throw "This script must be run as Administrator."
    }
}

function Backup-ExistingInstall {
    param(
        [string]$CurrentPath
    )

    if (-not (Test-Path $CurrentPath)) {
        return $null
    }

    $backupRoot = Join-Path ([Environment]::GetFolderPath("CommonApplicationData")) "Opti-Fresh\SizerDataCollector\backups"
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupPath = Join-Path $backupRoot ("install_" + $stamp)
    Write-Step "Backing up current install to '$backupPath'"
    Copy-Item -Path $CurrentPath -Destination $backupPath -Recurse -Force
    return $backupPath
}

function Copy-BuildArtifacts {
    param(
        [string]$RepoRoot,
        [string]$ConfigurationName,
        [string]$TargetRoot
    )

    $cliSource = Join-Path $RepoRoot ("bin\" + $ConfigurationName)
    $serviceSource = Join-Path $RepoRoot ("SizerDataCollector.Service\bin\" + $ConfigurationName)

    if (-not (Test-Path $cliSource)) {
        throw "CLI output folder not found: $cliSource"
    }
    if (-not (Test-Path $serviceSource)) {
        throw "Service output folder not found: $serviceSource"
    }

    $cliTarget = Join-Path $TargetRoot "cli"
    $serviceTarget = Join-Path $TargetRoot "service"
    New-Item -ItemType Directory -Path $cliTarget -Force | Out-Null
    New-Item -ItemType Directory -Path $serviceTarget -Force | Out-Null

    Write-Step "Copying CLI artifacts"
    Copy-Item -Path (Join-Path $cliSource "*") -Destination $cliTarget -Recurse -Force

    Write-Step "Copying service artifacts"
    Copy-Item -Path (Join-Path $serviceSource "*") -Destination $serviceTarget -Recurse -Force
}

function Install-OrUpdateService {
    param(
        [string]$Name,
        [string]$ServiceExePath
    )

    if (-not (Test-Path $ServiceExePath)) {
        throw "Service executable not found: $ServiceExePath"
    }

    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Step "Stopping existing service '$Name' if running"
        sc.exe stop $Name | Out-Null
        Start-Sleep -Seconds 2
        Write-Step "Updating service binary path"
        sc.exe config $Name binPath= "`"$ServiceExePath`"" start= auto | Out-Null
    }
    else {
        Write-Step "Creating service '$Name'"
        sc.exe create $Name binPath= "`"$ServiceExePath`"" start= auto DisplayName= "Opti-Fresh Sizer Data Collector" | Out-Null
    }

    sc.exe description $Name "Collects sizer telemetry and writes to TimescaleDB." | Out-Null
    sc.exe failure $Name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
}

function Run-Preflight {
    param(
        [string]$CliExe
    )

    Write-Step "Running preflight checks"
    & $CliExe preflight --format=text
    if ($LASTEXITCODE -ne 0) {
        throw "Preflight failed with exit code $LASTEXITCODE."
    }
}

Assert-Admin

$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceExe = Join-Path $InstallRoot "service\SizerDataCollector.Service.exe"
$cliExe = Join-Path $InstallRoot "cli\SizerDataCollector.exe"

if (-not $SkipBuild) {
    Write-Step "Building solution ($Configuration)"
    Push-Location $repoRoot
    dotnet build "SizerDataCollector.sln" -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        throw "Build failed."
    }
    Pop-Location
}

$backup = Backup-ExistingInstall -CurrentPath $InstallRoot
Write-Step "Deploying artifacts to '$InstallRoot'"
Copy-BuildArtifacts -RepoRoot $repoRoot -ConfigurationName $Configuration -TargetRoot $InstallRoot

Install-OrUpdateService -Name $ServiceName -ServiceExePath $serviceExe

if (-not $SkipPreflight) {
    Run-Preflight -CliExe $cliExe
}

if (-not $SkipStart) {
    Write-Step "Starting service '$ServiceName'"
    sc.exe start $ServiceName | Out-Null
}

Write-Step "Install/upgrade completed."
if ($backup) {
    Write-Host "Backup: $backup"
}
