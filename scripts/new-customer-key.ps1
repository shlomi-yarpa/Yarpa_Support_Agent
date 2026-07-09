<#
.SYNOPSIS
    Provisions a new Yarpa customer and API key directly in the database.

.DESCRIPTION
    Creates one row in YarpaAgent_Customers and one active row in YarpaAgent_ApiKeys.
    The raw API key is generated (or supplied), printed ONCE, and only its SHA-256 hash
    is stored — matching how the API authenticates incoming requests. Give the printed key
    to the technician to place in that pharmacy's Yarpa.Agent appsettings.json (Agent:ApiKey).

    Run this on the server (or any machine that can reach the SQL Server), after the schema
    has been created (see docs/operations.md -> Production runbook).

.PARAMETER CustomerName
    Human-readable customer/pharmacy name (shown in the dashboard).

.PARAMETER ConnectionString
    ADO.NET connection string to the crm_yarpa database (same one the API uses).

.PARAMETER ApiKey
    Optional explicit key. When omitted, a strong random key is generated.

.EXAMPLE
    ./scripts/new-customer-key.ps1 -CustomerName "בית מרקחת דוגמה" `
        -ConnectionString "Server=10.10.10.30,3460;Database=crm_yarpa;User Id=sa;Password=***;TrustServerCertificate=True"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CustomerName,
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [string]$ApiKey
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    # 256 bits of entropy expressed as lowercase hex, prefixed for readability.
    $ApiKey = "yk-" + ([guid]::NewGuid().ToString("N")) + ([guid]::NewGuid().ToString("N"))
}

# SHA-256 hex (lowercase) — must match Yarpa.Api YarpaDbContext.ComputeKeyHash.
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($ApiKey))
} finally {
    $sha.Dispose()
}
$keyHash = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })

$customerId = [guid]::NewGuid()
$apiKeyId   = [guid]::NewGuid()
$nowUtc     = [DateTime]::UtcNow

Add-Type -AssemblyName System.Data | Out-Null
$conn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$conn.Open()
try {
    $tx = $conn.BeginTransaction()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.Transaction = $tx
        $cmd.CommandText = @"
INSERT INTO YarpaAgent_Customers (CustomerId, Name, CreatedAtUtc)
VALUES (@CustomerId, @Name, @CreatedAtUtc);

INSERT INTO YarpaAgent_ApiKeys (ApiKeyId, CustomerId, KeyHash, IsActive, CreatedAtUtc, RevokedAtUtc)
VALUES (@ApiKeyId, @CustomerId, @KeyHash, 1, @CreatedAtUtc, NULL);
"@
        [void]$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@CustomerId",  [System.Data.SqlDbType]::UniqueIdentifier))); $cmd.Parameters["@CustomerId"].Value  = $customerId
        [void]$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@ApiKeyId",    [System.Data.SqlDbType]::UniqueIdentifier))); $cmd.Parameters["@ApiKeyId"].Value    = $apiKeyId
        [void]$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@Name",        [System.Data.SqlDbType]::NVarChar, 200))); $cmd.Parameters["@Name"].Value        = $CustomerName
        [void]$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@KeyHash",     [System.Data.SqlDbType]::NVarChar, 64)));  $cmd.Parameters["@KeyHash"].Value     = $keyHash
        [void]$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter("@CreatedAtUtc",[System.Data.SqlDbType]::DateTime2)));     $cmd.Parameters["@CreatedAtUtc"].Value= $nowUtc

        [void]$cmd.ExecuteNonQuery()
        $tx.Commit()
    } catch {
        $tx.Rollback()
        throw
    }
} finally {
    $conn.Close()
}

Write-Host ""
Write-Host "Customer provisioned successfully." -ForegroundColor Green
Write-Host "  CustomerId : $customerId"
Write-Host "  Name       : $CustomerName"
Write-Host ""
Write-Host "API KEY (store now — it is NOT recoverable later):" -ForegroundColor Yellow
Write-Host "  $ApiKey" -ForegroundColor Yellow
Write-Host ""
Write-Host "Put this value in that pharmacy's Yarpa.Agent appsettings.json under Agent:ApiKey."
