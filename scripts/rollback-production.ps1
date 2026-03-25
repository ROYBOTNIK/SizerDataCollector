param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$InstallRoot = "C:\Program Files\Opti-Fresh\SizerDataCollector",
    [string]$ServiceName = "SizerDataCollectorService",
    [switch]$StartService
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

Assert-Admin

if (-not (Test-Path $BackupPath)) {
    throw "Backup path does not exist: $BackupPath"
}

Write-Step "Stopping service '$ServiceName'"
sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2

Write-Step "Restoring install folder from '$BackupPath'"
if (Test-Path $InstallRoot) {
    Remove-Item -Path $InstallRoot -Recurse -Force
}
Copy-Item -Path $BackupPath -Destination $InstallRoot -Recurse -Force

$serviceExe = Join-Path $InstallRoot "service\SizerDataCollector.Service.exe"
if (-not (Test-Path $serviceExe)) {
    throw "Restored service executable missing: $serviceExe"
}

Write-Step "Updating service binary path"
sc.exe config $ServiceName binPath= "`"$serviceExe`"" start= auto | Out-Null

if ($StartService) {
    Write-Step "Starting service '$ServiceName'"
    sc.exe start $ServiceName | Out-Null
}

Write-Step "Rollback complete."
