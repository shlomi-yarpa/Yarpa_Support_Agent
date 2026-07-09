<#
.SYNOPSIS
    Installs the Yarpa Support API as a Windows Service.

.DESCRIPTION
    Registers Yarpa.Api.exe with the Service Control Manager using sc.exe, running it in the
    Production environment so it binds to the URL and connection string defined in
    appsettings.Production.json (HTTP on the closed network, e.g. http://0.0.0.0:8080).
    Must be run as Administrator.

.PARAMETER ExePath
    Full path to the published Yarpa.Api.exe. Defaults to dist/server/api/Yarpa.Api.exe.

.PARAMETER ServiceName
    Windows service name. Defaults to YarpaApi.

.EXAMPLE
    ./scripts/install-api-service.ps1 -ExePath S:\y_a\api\Yarpa.Api.exe
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$ServiceName = "YarpaApi",
    [string]$DisplayName = "Yarpa Support API"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run as Administrator."
}

if (-not $ExePath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $ExePath = Join-Path $repoRoot "dist/server/api/Yarpa.Api.exe"
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
# appsettings.Production.json (URLs, connection string, RequireHttps=false) is applied.
$binPath = "`"$ExePath`" --environment Production"

Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "$DisplayName" | Out-Null
sc.exe description $ServiceName "Receives diagnostics snapshots from Yarpa agents and serves the support API." | Out-Null

Write-Host "Starting service '$ServiceName'..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service installed and $($svc.Status)." -ForegroundColor Green
Write-Host "  Health:    curl http://localhost:8080/health"
Write-Host "  Manage:    sc.exe query $ServiceName / Stop-Service $ServiceName"
Write-Host "  Uninstall: scripts/uninstall-api-service.ps1"
