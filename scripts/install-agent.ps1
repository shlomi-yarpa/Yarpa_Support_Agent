<#
.SYNOPSIS
    Installs the Yarpa Support Agent on a client (pharmacy) machine.

.DESCRIPTION
    Technician-facing installer. Run it from the unzipped package folder in an ELEVATED
    PowerShell (Run as Administrator). It:
        1. Copies the agent files to the install directory.
        2. Writes the customer's API key into appsettings.json.
        3. Checks that the API is reachable.
        4. Runs a first collection (--once).
        5. Registers the weekly background Windows Service (unless -NoService).

.PARAMETER ApiKey
    The customer's API key, supplied by Yarpa support (looks like yk-...).
    Optional when the package already ships with a key baked into appsettings.json.

.PARAMETER ApiBaseUrl
    API base URL. When omitted, the value already baked into appsettings.json is used.

.PARAMETER SiteCustomerCode
    Optional customer/site identifier (e.g. CRM customer code). Helps support identify
    which pharmacy this machine belongs to in the dashboard.

.PARAMETER InstallDir
    Target directory. Defaults to "%ProgramFiles%\Yarpa\Agent".

.PARAMETER NoService
    Only copy files and run once; do not install the background service.

.EXAMPLE
    .\install-agent.ps1
    .\install-agent.ps1 -SiteCustomerCode 12345
    .\install-agent.ps1 -ApiKey yk-xxxxxxxx -SiteCustomerCode 12345
#>
[CmdletBinding()]
param(
    [string]$ApiKey,
    [string]$ApiBaseUrl,
    [string]$SiteCustomerCode,
    [string]$InstallDir = (Join-Path $env:ProgramFiles "Yarpa\Agent"),
    [switch]$NoService,
    [string]$ServiceName = "YarpaSupportAgent",
    [string]$DisplayName = "Yarpa Support Agent"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must be run as Administrator (right-click PowerShell -> Run as administrator)."
}

$sourceDir = $PSScriptRoot
$exeName   = "Yarpa.Agent.exe"
$sourceExe = Join-Path $sourceDir $exeName
if (-not (Test-Path $sourceExe)) {
    throw "Yarpa.Agent.exe not found next to this script ($sourceDir). Run install-agent.ps1 from inside the unzipped package."
}

Write-Host "Installing Yarpa Support Agent to '$InstallDir'..." -ForegroundColor Cyan

# Stop an existing service so files are not locked during copy.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# Copy everything in the package except this installer and the readme.
Get-ChildItem -Path $sourceDir -Exclude "install-agent.ps1", "README*.txt" |
    Copy-Item -Destination $InstallDir -Recurse -Force

# Write the API key (and optionally the base URL) into appsettings.json.
$cfgPath = Join-Path $InstallDir "appsettings.json"
if (-not (Test-Path $cfgPath)) {
    throw "appsettings.json was not copied to $InstallDir."
}
$json = Get-Content -Path $cfgPath -Raw | ConvertFrom-Json
if ($ApiKey) {
    $json.Agent.ApiKey = $ApiKey
}
if ($ApiBaseUrl) {
    $json.Agent.ApiBaseUrl = $ApiBaseUrl
}
if ($SiteCustomerCode) {
    $json.Agent.SiteCustomerCode = $SiteCustomerCode.Trim()
}

# Validate the effective key: either passed via -ApiKey or already baked into the package.
$effectiveKey = $json.Agent.ApiKey
if ([string]::IsNullOrWhiteSpace($effectiveKey) -or $effectiveKey -eq "REPLACE_WITH_CUSTOMER_API_KEY") {
    throw "No API key configured. Re-run with:  .\install-agent.ps1 -ApiKey <the key from Yarpa support>"
}

($json | ConvertTo-Json -Depth 32) | Set-Content -Path $cfgPath -Encoding UTF8

$effectiveUrl = $json.Agent.ApiBaseUrl
Write-Host "  API URL : $effectiveUrl"
Write-Host "  API key : set"
if ($json.Agent.SiteCustomerCode) {
    Write-Host "  Site customer code : $($json.Agent.SiteCustomerCode)"
}

# Connectivity check (non-fatal: the agent queues offline and retries if unreachable).
try {
    $healthUrl = ($effectiveUrl.TrimEnd('/')) + "/health"
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 10
    Write-Host "  API reachable: $healthUrl -> HTTP $($resp.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "  WARNING: API not reachable at $effectiveUrl. The agent will queue snapshots offline and retry later." -ForegroundColor Yellow
}

# First collection so a snapshot appears immediately in the dashboard.
$installedExe = Join-Path $InstallDir $exeName
Write-Host "Running first collection (--once)..." -ForegroundColor Cyan
& $installedExe --once
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Note: first collection exit code $LASTEXITCODE (a send failure is retried automatically)." -ForegroundColor Yellow
}

if ($NoService) {
    Write-Host ""
    Write-Host "Done (no service installed). Run manually with: `"$installedExe`" --once" -ForegroundColor Green
    return
}

# Register the background service (weekly collection in a night-time window).
if ($existing) {
    Write-Host "Removing previous service '$ServiceName'..." -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binPath = "`"$installedExe`" --service"
Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "$DisplayName" | Out-Null
sc.exe description $ServiceName "Collects technical diagnostics and sends them to the Yarpa Support API." | Out-Null
Start-Service -Name $ServiceName

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Installation complete. Service '$ServiceName' is $($svc.Status)." -ForegroundColor Green
Write-Host "  Install dir : $InstallDir"
Write-Host "  Logs        : $(Join-Path $InstallDir 'logs')"
Write-Host "  Uninstall   : Stop-Service $ServiceName ; sc.exe delete $ServiceName"
