param(
    [string]$InstallRoot = "C:\Program Files (x86)\OPTI-FRESH\CollectorAgent",
    [string]$ServiceName = "SizerDataCollectorService",
    [switch]$SkipStart,
    [switch]$RunDbInit
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
        throw "Run this script in an elevated (Administrator) PowerShell."
    }
}

Assert-Admin

$bundleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$serviceSource = Join-Path $bundleRoot "service"
$serviceTarget = Join-Path $InstallRoot "service"

if (-not (Test-Path $serviceSource)) {
    throw "Bundle is missing 'service' folder at: $serviceSource"
}

Write-Step "Stopping service '$ServiceName' before file copy (if present)"
sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2

Write-Step "Deploying service files to '$serviceTarget'"
New-Item -ItemType Directory -Path $serviceTarget -Force | Out-Null
Copy-Item -Path (Join-Path $serviceSource "*") -Destination $serviceTarget -Recurse -Force

$serviceExe = Join-Path $serviceTarget "SizerDataCollector.Service.exe"
if (-not (Test-Path $serviceExe)) {
    throw "Service executable missing after copy: $serviceExe"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Updating existing service '$ServiceName' binary path"
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe config $ServiceName binPath= "`"$serviceExe`"" start= auto | Out-Null
}
else {
    Write-Step "Installing service '$ServiceName'"
    & $serviceExe service install
    if ($LASTEXITCODE -ne 0) {
        throw "Service install failed with exit code $LASTEXITCODE"
    }
}

if ($RunDbInit) {
    Write-Step "Running db init"
    & $serviceExe db init
    if ($LASTEXITCODE -ne 0) {
        throw "db init failed with exit code $LASTEXITCODE"
    }
}

if (-not $SkipStart) {
    Write-Step "Starting service '$ServiceName'"
    sc.exe start $ServiceName | Out-Null
}

Write-Step "Install complete."
Write-Host "Service EXE: $serviceExe"
