<#
.SYNOPSIS
    Builds a single double-click installer EXE for the Yarpa Support Agent.

.DESCRIPTION
    Publishes the self-contained agent, bakes in the production API URL (and
    optionally a shared API key), then wraps everything into ONE self-extracting
    executable using IEXPRESS.EXE (built into every Windows machine - no extra
    tooling required). The technician just needs to double-click the resulting
    .exe; it extracts to a temp folder, prompts for an optional site customer
    code, elevates via UAC, and runs install-agent.ps1 automatically.

.PARAMETER ApiBaseUrl
    The production API URL the agents will send to, e.g. http://10.10.10.206:8080

.PARAMETER ApiKey
    Optional shared API key to bake in. If omitted, the technician must pass
    -ApiKey when running install-agent.ps1 manually (not applicable for the
    double-click flow, so normally you SHOULD supply this).

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputExe
    Output exe path. Defaults to dist/YarpaAgentInstaller.exe in the repo root.

.EXAMPLE
    ./scripts/build-agent-installer-exe.ps1 -ApiBaseUrl http://10.10.10.206:8080 -ApiKey yk-xxxx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [string]$ApiKey,
    [string]$Configuration = "Release",
    [string]$OutputExe
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$staging  = Join-Path $repoRoot "dist/agent-installer-exe"

if (-not $OutputExe) {
    $OutputExe = Join-Path $repoRoot "dist/YarpaAgentInstaller.exe"
}
$OutputExe = [System.IO.Path]::GetFullPath($OutputExe)

if (Test-Path $staging) {
    Remove-Item -Recurse -Force $staging
}

# 1. Publish the self-contained agent straight into the staging folder.
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
    Write-Host "WARNING: No -ApiKey supplied. The baked-in placeholder will make install-agent.ps1" -ForegroundColor Yellow
    Write-Host "         fail with 'No API key configured' when technicians double-click the exe," -ForegroundColor Yellow
    Write-Host "         because there is no way to pass -ApiKey through a double-click flow." -ForegroundColor Yellow
}
($json | ConvertTo-Json -Depth 32) | Set-Content -Path $cfgPath -Encoding UTF8

# 3. IEXPRESS extracts everything into ONE flat folder (no subfolders), so the
#    config\payment-terminal-vendors.json file must be flattened here; Run-Installer.cmd
#    puts it back under config\ right before installing.
$configFile = Join-Path $staging "config\payment-terminal-vendors.json"
if (Test-Path $configFile) {
    Move-Item -Force $configFile (Join-Path $staging "payment-terminal-vendors.json")
    Remove-Item -Recurse -Force (Join-Path $staging "config")
}

# 4. Add the double-click launcher, the installer script, and the Hebrew instructions.
Copy-Item -Path (Join-Path $PSScriptRoot "Run-Installer.cmd") -Destination $staging -Force
Copy-Item -Path (Join-Path $PSScriptRoot "install-agent.ps1") -Destination $staging -Force
Copy-Item -Path (Join-Path $PSScriptRoot "agent-package-readme.txt") `
          -Destination (Join-Path $staging "README-install.txt") -Force

# 5. Build the IEXPRESS .SED script describing the package, then invoke iexpress.exe.
$files = Get-ChildItem -Path $staging -File
if ($files.Count -eq 0) {
    throw "No files found in staging folder '$staging'."
}

$fileLines       = New-Object System.Collections.Generic.List[string]
$sourceFileLines = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $files.Count; $i++) {
    $fileLines.Add("FILE$i=`"$($files[$i].Name)`"")
    $sourceFileLines.Add("%FILE$i%=")
}

$sedPath = Join-Path $staging "package.sed"
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles

[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$OutputExe
FriendlyName=Yarpa Support Agent Installer
AppLaunched=Run-Installer.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
$([string]::Join("`r`n", $fileLines))

[SourceFiles]
SourceFiles0=$staging\

[SourceFiles0]
$([string]::Join("`r`n", $sourceFileLines))
"@

Set-Content -Path $sedPath -Value $sed -Encoding ASCII

if (Test-Path $OutputExe) {
    Remove-Item -Force $OutputExe
}
$outDir = Split-Path -Parent $OutputExe
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

Write-Host "Building self-extracting installer with IEXPRESS..." -ForegroundColor Cyan
$proc = Start-Process -FilePath "iexpress.exe" -ArgumentList @("/N", "/Q", $sedPath) -PassThru
$proc.WaitForExit()

# iexpress.exe can return control to the parent slightly before the finished .exe
# is flushed to disk; poll briefly instead of checking immediately.
$deadline = (Get-Date).AddSeconds(30)
while (-not (Test-Path $OutputExe) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}
if (-not (Test-Path $OutputExe)) {
    throw "iexpress.exe did not produce '$OutputExe' within 30 seconds."
}

$sizeMb = [math]::Round((Get-Item $OutputExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer built." -ForegroundColor Green
Write-Host "  File    : $OutputExe ($sizeMb MB)"
Write-Host "  API URL : $ApiBaseUrl (baked in)"
Write-Host ""
Write-Host "Hand this single .exe to the technician. On the client machine:" -ForegroundColor Cyan
Write-Host "  1. Copy/download YarpaAgentInstaller.exe."
Write-Host "  2. Double-click it."
Write-Host "  3. Optionally type the site/customer code when prompted, press Enter."
Write-Host "  4. Click 'Yes' on the Windows security (UAC) prompt."
Write-Host "  Done - the agent is installed and running as a service."
