<#
.SYNOPSIS
    Publishes the Yarpa server side (API + Dashboard) as self-contained win-x64 apps.

.DESCRIPTION
    Produces two ready-to-run folders that need no .NET runtime installed on the server:
        <OutputDir>\api        -> Yarpa.Api.exe        (REST API, listens on http://0.0.0.0:8080)
        <OutputDir>\dashboard  -> Yarpa.Dashboard.exe   (support UI, listens on http://0.0.0.0:8081)

    Copy both folders to the production server (e.g. S:\y_a\api and S:\y_a\dashboard),
    edit each appsettings.Production.json with the real connection string / URLs / API key,
    then install them as Windows Services with scripts/install-api-service.ps1 and
    scripts/install-dashboard-service.ps1.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputDir
    Output directory. Defaults to dist/server in the repo root.

.EXAMPLE
    ./scripts/publish-server.ps1
    ./scripts/publish-server.ps1 -OutputDir S:\y_a
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent $PSScriptRoot
$apiProject   = Join-Path $repoRoot "src/Yarpa.Api/Yarpa.Api.csproj"
$dashProject  = Join-Path $repoRoot "src/Yarpa.Dashboard/Yarpa.Dashboard.csproj"

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "dist/server"
}

$apiOut  = Join-Path $OutputDir "api"
$dashOut = Join-Path $OutputDir "dashboard"

function Publish-One {
    param([string]$Project, [string]$Destination, [string]$Label)

    Write-Host "Publishing $Label (self-contained, win-x64)..." -ForegroundColor Cyan
    Write-Host "  Project : $Project"
    Write-Host "  Output  : $Destination"

    if (Test-Path $Destination) {
        Remove-Item -Recurse -Force $Destination
    }

    dotnet publish $Project `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $Destination

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Label with exit code $LASTEXITCODE"
    }
}

Publish-One -Project $apiProject  -Destination $apiOut  -Label "Yarpa.Api"
Publish-One -Project $dashProject -Destination $dashOut -Label "Yarpa.Dashboard"

Write-Host ""
Write-Host "Server publish complete." -ForegroundColor Green
Write-Host "  API       : $(Join-Path $apiOut  'Yarpa.Api.exe')"
Write-Host "  Dashboard : $(Join-Path $dashOut 'Yarpa.Dashboard.exe')"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Copy '$apiOut' and '$dashOut' to the server (e.g. S:\y_a\api and S:\y_a\dashboard)."
Write-Host "  2. Edit S:\y_a\api\appsettings.Production.json       -> ConnectionStrings.Default (crm_yarpa)."
Write-Host "  3. Edit S:\y_a\dashboard\appsettings.Production.json -> ApiSettings.BaseUrl + ApiSettings.ApiKey."
Write-Host "  4. Apply the database schema (see docs/operations.md -> Production runbook)."
Write-Host "  5. Install services (run as Administrator):"
Write-Host "       scripts\install-api-service.ps1       -ExePath S:\y_a\api\Yarpa.Api.exe"
Write-Host "       scripts\install-dashboard-service.ps1 -ExePath S:\y_a\dashboard\Yarpa.Dashboard.exe"
