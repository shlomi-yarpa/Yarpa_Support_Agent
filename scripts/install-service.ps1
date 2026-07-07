<#
.SYNOPSIS
    Installs the Yarpa Support Agent as a Windows Service.

.DESCRIPTION
    Registers Yarpa.Agent.exe with the Service Control Manager using sc.exe, running it
    with the --service flag so it collects and sends snapshots on the configured schedule
    (Agent:Service:IntervalHours in appsettings.json). Must be run as Administrator.

.PARAMETER ExePath
    Full path to the published Yarpa.Agent.exe. Defaults to dist/agent/win-x64/Yarpa.Agent.exe.

.PARAMETER ServiceName
    Windows service name. Defaults to YarpaSupportAgent.

.EXAMPLE
    ./scripts/install-service.ps1
    ./scripts/install-service.ps1 -ExePath "C:\Program Files\Yarpa\Agent\Yarpa.Agent.exe"
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$ServiceName = "YarpaSupportAgent",
    [string]$DisplayName = "Yarpa Support Agent"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run as Administrator."
}

if (-not $ExePath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $ExePath = Join-Path $repoRoot "dist/agent/win-x64/Yarpa.Agent.exe"
}

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath. Run scripts/publish-agent.ps1 first."
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

# binPath must quote the exe and pass --service so the host runs the background worker.
$binPath = "`"$ExePath`" --service"

Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "$DisplayName" | Out-Null
sc.exe description $ServiceName "Collects technical diagnostics and sends them to the Yarpa Support API." | Out-Null

Write-Host "Starting service '$ServiceName'..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service installed and $($svc.Status)." -ForegroundColor Green
Write-Host "  Manage:    sc.exe query $ServiceName / Stop-Service $ServiceName"
Write-Host "  Uninstall: scripts/uninstall-service.ps1"
