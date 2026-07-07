using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Contracts;

namespace Yarpa.Agent;

/// <summary>
/// Serializes a <see cref="DiagnosticsSnapshot"/> and POSTs it to the API.
/// The named HttpClient "YarpaApi" is configured with Polly retry at registration time.
/// When all retries fail, the snapshot is persisted to the <see cref="OfflineQueue"/>.
/// On startup the caller should call <see cref="DrainOfflineQueueAsync"/> first.
/// </summary>
public sealed class SnapshotSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OfflineQueue _offlineQueue;
    private readonly ILogger<SnapshotSender> _logger;

    internal const string HttpClientName = "YarpaApi";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public SnapshotSender(
        IHttpClientFactory httpClientFactory,
        OfflineQueue offlineQueue,
        ILogger<SnapshotSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _offlineQueue = offlineQueue;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to send <paramref name="snapshot"/> to the API.
    /// Returns true on HTTP 2xx; queues the snapshot and returns false on network/server failure.
    /// Server-side 4xx errors are logged but not queued (they are not retriable).
    /// </summary>
    public async Task<bool> SendAsync(DiagnosticsSnapshot snapshot, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(snapshot, SerializerOptions);

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(
                "/api/v1/snapshots", content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "snapshot {SnapshotId} accepted by API (status={Status})",
                    snapshot.SnapshotId, (int)response.StatusCode);
                return true;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogError(
                    "API rejected snapshot {SnapshotId}: 401 Unauthorized – check ApiKey configuration",
                    snapshot.SnapshotId);
                return false; // not retriable
            }

            _logger.LogWarning(
                "API returned non-success status {Status} for snapshot {SnapshotId}; saving to offline queue",
                (int)response.StatusCode, snapshot.SnapshotId);

            await _offlineQueue.EnqueueAsync(snapshot, ct);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine shutdown/cancellation requested by the caller — let it propagate.
            throw;
        }
        catch (Exception ex)
        {
            // Any other failure (including HttpClient.Timeout, which surfaces as a
            // TaskCanceledException without our token being cancelled) is transient:
            // queue the snapshot so it is never lost and retry on the next run.
            _logger.LogError(ex,
                "failed to send snapshot {SnapshotId} after all retries; saving to offline queue",
                snapshot.SnapshotId);
            await _offlineQueue.EnqueueAsync(snapshot, ct);
            return false;
        }
    }

    /// <summary>
    /// Processes all snapshots waiting in the offline queue and attempts to resend them.
    /// Each successfully sent snapshot is removed from the queue.
    /// </summary>
    public async Task DrainOfflineQueueAsync(CancellationToken ct)
    {
        IReadOnlyList<(string Path, string Json)> pending = _offlineQueue.GetPendingItems();
        if (pending.Count == 0)
            return;

        _logger.LogInformation("offline queue: {Count} snapshot(s) pending; draining now", pending.Count);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        foreach ((string filePath, string json) in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(
                    "/api/v1/snapshots", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    _offlineQueue.Dequeue(filePath);
                    _logger.LogInformation(
                        "queued snapshot sent successfully (status={Status}), removed from queue",
                        (int)response.StatusCode);
                }
                else
                {
                    _logger.LogWarning(
                        "queued snapshot rejected (status={Status}); will retry on next run",
                        (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Genuine shutdown/cancellation — stop draining and let it propagate.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "queued snapshot at {FilePath} could not be sent; will retry on next run",
                    filePath);
            }
        }
    }
}
