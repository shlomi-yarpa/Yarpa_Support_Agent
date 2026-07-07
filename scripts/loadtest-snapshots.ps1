<#
.SYNOPSIS
    Basic load test for POST /api/v1/snapshots against a running API.

.DESCRIPTION
    Sends many concurrent snapshots (payload based on snapshot.json), each with a fresh
    snapshotId and a distinct machineId, and reports throughput, status distribution and
    response-time percentiles. Use to validate the API under concurrent load in a real
    (SQL Server backed) environment.

.PARAMETER BaseUrl
    Base URL of the API, e.g. https://localhost:7177.

.PARAMETER ApiKey
    A valid customer API key for the X-Api-Key header.

.PARAMETER Total
    Total number of requests. Default 200.

.PARAMETER Concurrency
    Maximum number of in-flight requests. Default 20.

.PARAMETER PayloadFile
    Snapshot JSON used as the payload template. Defaults to snapshot.json in the repo root.

.EXAMPLE
    ./scripts/loadtest-snapshots.ps1 -BaseUrl https://localhost:7177 -ApiKey dev-yarpa-api-key-2026
    ./scripts/loadtest-snapshots.ps1 -BaseUrl https://host -ApiKey <key> -Total 500 -Concurrency 50
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$ApiKey,
    [int]$Total = 200,
    [int]$Concurrency = 20,
    [string]$PayloadFile
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PayloadFile) {
    $PayloadFile = Join-Path $repoRoot "snapshot.json"
}
if (-not (Test-Path $PayloadFile)) {
    throw "Payload file not found: $PayloadFile"
}

$template = Get-Content -Raw -Path $PayloadFile | ConvertFrom-Json
$endpoint = "$($BaseUrl.TrimEnd('/'))/api/v1/snapshots"
$payloadBytes = [System.Text.Encoding]::UTF8.GetByteCount((Get-Content -Raw -Path $PayloadFile))

Write-Host "Load test -> $endpoint" -ForegroundColor Cyan
Write-Host "  Requests   : $Total (concurrency $Concurrency)"
Write-Host "  Payload    : ~$([math]::Round($payloadBytes/1KB,1)) KB each"

$job = {
    param($endpoint, $apiKey, $json)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-WebRequest -Uri $endpoint -Method Post -Body $json `
            -ContentType "application/json" -Headers @{ "X-Api-Key" = $apiKey } `
            -SkipHttpErrorCheck -SkipCertificateCheck
        $status = [int]$resp.StatusCode
    } catch {
        $status = -1
    }
    $sw.Stop()
    [pscustomobject]@{ Status = $status; Ms = $sw.Elapsed.TotalMilliseconds }
}

$results = [System.Collections.Concurrent.ConcurrentBag[object]]::new()
$throttle = [System.Threading.SemaphoreSlim]::new($Concurrency)
$tasks = @()

$swAll = [System.Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt $Total; $i++) {
    $throttle.Wait()
    $clone = $template | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $clone.snapshotId = [guid]::NewGuid().ToString()
    $clone.machineId = "loadtest-machine-{0:D5}" -f $i
    $clone.collectedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    $json = $clone | ConvertTo-Json -Depth 40 -Compress

    $ps = [powershell]::Create().AddScript($job).AddArgument($endpoint).AddArgument($ApiKey).AddArgument($json)
    $handle = $ps.BeginInvoke()
    $tasks += [pscustomobject]@{ Ps = $ps; Handle = $handle }

    # Simple drain to respect concurrency
    $completed = $tasks | Where-Object { $_.Handle.IsCompleted }
    foreach ($t in $completed) {
        $results.Add($t.Ps.EndInvoke($t.Handle)[0]); $t.Ps.Dispose(); $throttle.Release()
    }
    $tasks = $tasks | Where-Object { -not $_.Handle.IsCompleted }
}

foreach ($t in $tasks) {
    $results.Add($t.Ps.EndInvoke($t.Handle)[0]); $t.Ps.Dispose(); $throttle.Release()
}
$swAll.Stop()

$all = @($results)
$byStatus = $all | Group-Object Status | Sort-Object Name
$times = $all.Ms | Sort-Object
$p = { param($pct) if ($times.Count -eq 0) { 0 } else { $times[[math]::Min($times.Count-1, [int][math]::Floor($pct/100.0*$times.Count))] } }
$perSec = $Total / [math]::Max(0.001, $swAll.Elapsed.TotalSeconds)

Write-Host ""
Write-Host "Results:" -ForegroundColor Green
Write-Host "  Elapsed     : $([math]::Round($swAll.Elapsed.TotalSeconds,2)) s"
Write-Host "  Throughput  : $([math]::Round($perSec,1)) req/s"
foreach ($g in $byStatus) { Write-Host "  HTTP $($g.Name)    : $($g.Count)" }
Write-Host "  Latency ms  : p50=$([math]::Round((& $p 50),1)) p95=$([math]::Round((& $p 95),1)) p99=$([math]::Round((& $p 99),1))"

$failures = ($all | Where-Object { $_.Status -ge 500 -or $_.Status -eq -1 }).Count
if ($failures -gt 0) {
    Write-Host "  FAILURES    : $failures" -ForegroundColor Red
    exit 1
}
Write-Host "  No 5xx/connection failures." -ForegroundColor Green
