using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Yarpa.Contracts;

namespace Yarpa.Agent;

/// <summary>
/// Long-lived background worker used when the Agent runs as a Windows Service.
/// On each cycle it drains the offline queue, collects a fresh snapshot and sends it,
/// then waits for the configured interval. Every cycle is fully isolated: an unexpected
/// error is logged and the loop continues, so the service never crashes.
/// The one-shot CLI modes (--once / --dry-run / --output) do not use this worker.
/// </summary>
public sealed class SnapshotWorker : BackgroundService
{
    private readonly CollectionOrchestrator _orchestrator;
    private readonly SnapshotSender _sender;
    private readonly ServiceOptions _serviceOptions;
    private readonly ILogger<SnapshotWorker> _logger;

    public SnapshotWorker(
        CollectionOrchestrator orchestrator,
        SnapshotSender sender,
        IOptions<AgentOptions> options,
        ILogger<SnapshotWorker> logger)
    {
        _orchestrator = orchestrator;
        _sender = sender;
        _serviceOptions = options.Value.Service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = ResolveInterval();

        _logger.LogInformation(
            "Yarpa Agent service started (intervalHours={IntervalHours}, runOnStart={RunOnStart})",
            interval.TotalHours, _serviceOptions.RunImmediatelyOnStart);

        if (!_serviceOptions.RunImmediatelyOnStart)
        {
            if (!await DelayAsync(interval, stoppingToken))
                return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);

            if (!await DelayAsync(interval, stoppingToken))
                return;
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await _sender.DrainOfflineQueueAsync(ct);

            DiagnosticsSnapshot snapshot = await _orchestrator.CollectAsync(ct);

            using (LogContext.PushProperty("SnapshotId", snapshot.SnapshotId))
            using (LogContext.PushProperty("MachineId", snapshot.MachineId))
            {
                await _sender.SendAsync(snapshot, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "collection cycle failed; will retry on the next interval");
        }
    }

    private TimeSpan ResolveInterval()
    {
        double hours = _serviceOptions.IntervalHours > 0 ? _serviceOptions.IntervalHours : 6;
        return TimeSpan.FromHours(hours);
    }

    /// <summary>
    /// Waits for <paramref name="delay"/> or until cancellation.
    /// Returns false if cancellation was requested (caller should stop).
    /// </summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
