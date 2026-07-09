<#
.SYNOPSIS
    Installs the Yarpa Support Dashboard as a Windows Service.

.DESCRIPTION
    Registers Yarpa.Dashboard.exe with the Service Control Manager using sc.exe, running it in
    the Production environment so it binds to the URL defined in appsettings.Production.json
    (e.g. http://0.0.0.0:8081) and talks to the API via ApiSettings.BaseUrl.
    Must be run as Administrator.

.PARAMETER ExePath
    Full path to the published Yarpa.Dashboard.exe. Defaults to dist/server/dashboard/Yarpa.Dashboard.exe.

.PARAMETER ServiceName
    Windows service name. Defaults to YarpaDashboard.

.EXAMPLE
    ./scripts/install-dashboard-service.ps1 -ExePath S:\y_a\dashboard\Yarpa.Dashboard.exe
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$ServiceName = "YarpaDashboard",
    [string]$DisplayName = "Yarpa Support Dashboard"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run as Administrator."
}

if (-not $ExePath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $ExePath = Join-Path $repoRoot "dist/server/dashboard/Yarpa.Dashboard.exe"
}

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath. Run scripts/publish-server.ps1 first."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# binPath must quote the exe and select the Production environment so
# appsettings.Production.json (URL, API base address, API key) is applied.
# New-Service (not sc.exe) so a quoted, space-containing path is passed through correctly.
$binPath = "`"$ExePath`" --environment Production"

Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName $binPath `
    -DisplayName $DisplayName `
    -Description "Serves the Yarpa support dashboard UI for viewing customer diagnostics." `
    -StartupType Automatic | Out-Null

Write-Host "Starting service '$ServiceName'..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service installed and $($svc.Status)." -ForegroundColor Green
Write-Host "  Open:      http://localhost:8081"
Write-Host "  Manage:    sc.exe query $ServiceName / Stop-Service $ServiceName"
Write-Host "  Uninstall: scripts/uninstall-dashboard-service.ps1"
