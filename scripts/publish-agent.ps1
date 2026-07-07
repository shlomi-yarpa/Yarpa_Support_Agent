<#
.SYNOPSIS
    Publishes the Yarpa Agent as a self-contained, single-file win-x64 executable.

.DESCRIPTION
    Produces a single Yarpa.Agent.exe (with the embedded application icon) that runs on
    a client machine without any .NET runtime installed. The application settings file
    is copied next to the executable so the customer can configure ApiBaseUrl / ApiKey.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputDir
    Optional explicit output directory. Defaults to dist/agent/win-x64 in the repo root.

.EXAMPLE
    ./scripts/publish-agent.ps1
    ./scripts/publish-agent.ps1 -OutputDir C:\Temp\YarpaAgent
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot "src/Yarpa.Agent/Yarpa.Agent.csproj"

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "dist/agent/win-x64"
}

Write-Host "Publishing Yarpa Agent (self-contained, single-file, win-x64)..." -ForegroundColor Cyan
Write-Host "  Project : $project"
Write-Host "  Output  : $OutputDir"

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $OutputDir "Yarpa.Agent.exe"
if (-not (Test-Path $exe)) {
    throw "Expected executable not found: $exe"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "  Executable : $exe ($sizeMb MB)"
Write-Host "  Settings   : $(Join-Path $OutputDir 'appsettings.json')"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Edit appsettings.json: set Agent.ApiBaseUrl and Agent.ApiKey."
Write-Host "  2. Run once:      .\Yarpa.Agent.exe --once"
Write-Host "  3. Install service: scripts/install-service.ps1 (run as Administrator)."
