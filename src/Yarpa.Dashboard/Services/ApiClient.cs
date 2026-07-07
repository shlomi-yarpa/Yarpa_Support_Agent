using System.Net.Http.Json;
using System.Text.Json;
using Yarpa.Dashboard.Models;

namespace Yarpa.Dashboard.Services;

/// <summary>
/// Typed HTTP client that wraps all calls to the Yarpa REST API.
/// Configured in DI with the base address and X-Api-Key header.
/// All methods return null on 404 and throw ApiException on other non-success statuses.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    // ── Machines ──────────────────────────────────────────────────────────────

    public async Task<MachinesPage?> GetMachinesAsync(
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        string url = $"api/v1/machines?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";

        return await GetAsync<MachinesPage>(url, ct);
    }

    public async Task<MachineSummary?> GetSummaryAsync(
        string machineId,
        CancellationToken ct = default)
        => await GetAsync<MachineSummary>(
               $"api/v1/machines/{Uri.EscapeDataString(machineId)}/summary", ct);

    public async Task<SnapshotsPage?> GetSnapshotsAsync(
        string machineId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => await GetAsync<SnapshotsPage>(
               $"api/v1/machines/{Uri.EscapeDataString(machineId)}/snapshots?page={page}&pageSize={pageSize}", ct);

    public async Task<ChangesPage?> GetChangesAsync(
        string machineId,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => await GetAsync<ChangesPage>(
               $"api/v1/machines/{Uri.EscapeDataString(machineId)}/changes?page={page}&pageSize={pageSize}", ct);

    public async Task<AlertsPage?> GetAlertsAsync(
        string machineId,
        string state = "all",
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => await GetAsync<AlertsPage>(
               $"api/v1/machines/{Uri.EscapeDataString(machineId)}/alerts?state={Uri.EscapeDataString(state)}&page={page}&pageSize={pageSize}", ct);

    // ── Raw snapshot ──────────────────────────────────────────────────────────

    public async Task<string?> GetRawSnapshotAsync(
        Guid snapshotId,
        CancellationToken ct = default)
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"api/v1/snapshots/{snapshotId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        string raw = await response.Content.ReadAsStringAsync(ct);
        // Pretty-print the raw JSON for display
        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        HttpResponseMessage response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            throw new ApiException((int)response.StatusCode,
                $"API error {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }
}

/// <summary>Thrown when the API returns a non-success, non-404 status code.</summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
