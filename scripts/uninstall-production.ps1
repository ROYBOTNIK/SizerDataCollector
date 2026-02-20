param(
    [string]$ServiceName = "SizerDataCollectorService",
    [switch]$RemoveInstallFolder,
    [string]$InstallRoot = "C:\Program Files\Opti-Fresh\SizerDataCollector"
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

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Stopping service '$ServiceName'"
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Step "Deleting service '$ServiceName'"
    sc.exe delete $ServiceName | Out-Null
}
else {
    Write-Step "Service '$ServiceName' not found. Nothing to delete."
}

if ($RemoveInstallFolder -and (Test-Path $InstallRoot)) {
    Write-Step "Removing install folder '$InstallRoot'"
    Remove-Item -Path $InstallRoot -Recurse -Force
}

Write-Step "Uninstall complete."
