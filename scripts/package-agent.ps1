<#
.SYNOPSIS
    Builds a ready-to-hand-out Yarpa Agent installation package (ZIP) for technicians.

.DESCRIPTION
    Publishes the self-contained agent, bakes the production API URL into appsettings.json,
    bundles the technician installer (install-agent.ps1) and Hebrew instructions, and zips
    everything into a single file. The per-pharmacy API key is NOT baked in — the technician
    supplies it at install time (-ApiKey), so one package fits every customer.

.PARAMETER ApiBaseUrl
    The production API URL the agents will send to, e.g. http://10.10.10.30:8080

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputZip
    Output zip path. Defaults to dist/YarpaAgent-Setup.zip in the repo root.

.EXAMPLE
    ./scripts/package-agent.ps1 -ApiBaseUrl http://10.10.10.30:8080
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [string]$ApiKey,
    [string]$Configuration = "Release",
    [string]$OutputZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$staging  = Join-Path $repoRoot "dist/agent-package"

if (-not $OutputZip) {
    $OutputZip = Join-Path $repoRoot "dist/YarpaAgent-Setup.zip"
}

# 1. Publish the self-contained agent into the staging folder.
& (Join-Path $PSScriptRoot "publish-agent.ps1") -Configuration $Configuration -OutputDir $staging
if ($LASTEXITCODE -ne 0) {
    throw "publish-agent.ps1 failed with exit code $LASTEXITCODE"
}

# 2. Bake the API URL, and either the shared key (if supplied) or a clear placeholder.
$cfgPath = Join-Path $staging "appsettings.json"
$json = Get-Content -Path $cfgPath -Raw | ConvertFrom-Json
$json.Agent.ApiBaseUrl = $ApiBaseUrl
if ($ApiKey) {
    $json.Agent.ApiKey = $ApiKey
} else {
    $json.Agent.ApiKey = "REPLACE_WITH_CUSTOMER_API_KEY"
}
($json | ConvertTo-Json -Depth 32) | Set-Content -Path $cfgPath -Encoding UTF8

# 3. Add the technician installer and the Hebrew instructions.
Copy-Item -Path (Join-Path $PSScriptRoot "install-agent.ps1") -Destination $staging -Force
Copy-Item -Path (Join-Path $PSScriptRoot "agent-package-readme.txt") `
          -Destination (Join-Path $staging "README-install.txt") -Force

# 4. Zip the whole staging folder.
if (Test-Path $OutputZip) {
    Remove-Item -Force $OutputZip
}
$outDir = Split-Path -Parent $OutputZip
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputZip

$sizeMb = [math]::Round((Get-Item $OutputZip).Length / 1MB, 1)
Write-Host ""
Write-Host "Agent package created." -ForegroundColor Green
Write-Host "  Package : $OutputZip ($sizeMb MB)"
Write-Host "  API URL : $ApiBaseUrl (baked into appsettings.json)"
Write-Host ""
if ($ApiKey) {
    Write-Host "A shared API key is baked into the package. On the client:" -ForegroundColor Cyan
    Write-Host "  1. Unzip it."
    Write-Host "  2. Open PowerShell as Administrator in the unzipped folder."
    Write-Host "  3. Run:  .\install-agent.ps1"
} else {
    Write-Host "Hand the ZIP to the technician together with the API key. On the client:" -ForegroundColor Cyan
    Write-Host "  1. Unzip it."
    Write-Host "  2. Open PowerShell as Administrator in the unzipped folder."
    Write-Host "  3. Run:  .\install-agent.ps1 -ApiKey <the key>"
}
