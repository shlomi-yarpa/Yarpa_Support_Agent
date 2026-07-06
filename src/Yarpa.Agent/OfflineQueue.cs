using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Yarpa.Contracts;

namespace Yarpa.Agent;

/// <summary>
/// File-based queue for snapshots that could not be sent due to network failures.
/// Each snapshot is stored as a JSON file named by its <see cref="DiagnosticsSnapshot.SnapshotId"/>.
/// On the next run, the caller drains the queue before collecting a new snapshot.
/// </summary>
public sealed class OfflineQueue
{
    private readonly string _queueDirectory;
    private readonly ILogger<OfflineQueue> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public OfflineQueue(IOptions<AgentOptions> options, ILogger<OfflineQueue> logger)
    {
        _queueDirectory = string.IsNullOrWhiteSpace(options.Value.OfflineQueuePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yarpa", "OfflineQueue")
            : options.Value.OfflineQueuePath;

        _logger = logger;
    }

    /// <summary>Persists a snapshot to the queue directory.</summary>
    public async Task EnqueueAsync(DiagnosticsSnapshot snapshot, CancellationToken ct)
    {
        Directory.CreateDirectory(_queueDirectory);
        string filePath = Path.Combine(_queueDirectory, $"{snapshot.SnapshotId}.json");

        string json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        await File.WriteAllTextAsync(filePath, json, ct);

        _logger.LogInformation(
            "snapshot {SnapshotId} saved to offline queue: {FilePath}",
            snapshot.SnapshotId, filePath);
    }

    /// <summary>
    /// Returns all pending (path, rawJson) pairs ordered oldest-first.
    /// </summary>
    public IReadOnlyList<(string Path, string Json)> GetPendingItems()
    {
        if (!Directory.Exists(_queueDirectory))
            return Array.Empty<(string, string)>();

        return Directory
            .GetFiles(_queueDirectory, "*.json")
            .OrderBy(File.GetCreationTimeUtc)
            .Select(p =>
            {
                try { return (Path: p, Json: File.ReadAllText(p)); }
                catch { return (Path: p, Json: string.Empty); }
            })
            .Where(t => !string.IsNullOrEmpty(t.Json))
            .ToList();
    }

    /// <summary>Removes a successfully sent file from the queue.</summary>
    public void Dequeue(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "could not delete queued file {FilePath}", filePath);
        }
    }
}
