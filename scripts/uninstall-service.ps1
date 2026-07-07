<#
.SYNOPSIS
    Stops and removes the Yarpa Support Agent Windows Service.

.PARAMETER ServiceName
    Windows service name. Defaults to YarpaSupportAgent.

.EXAMPLE
    ./scripts/uninstall-service.ps1
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "YarpaSupportAgent"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run as Administrator."
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service '$ServiceName' is not installed. Nothing to do." -ForegroundColor Yellow
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
}

Write-Host "Removing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Null

Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
