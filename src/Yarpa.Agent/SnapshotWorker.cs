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

    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_serviceOptions.IntervalHours > 0)
            _logger.LogInformation(
                "Yarpa Agent service started (fixed interval={IntervalHours}h, runOnStart={RunOnStart})",
                _serviceOptions.IntervalHours, _serviceOptions.RunImmediatelyOnStart);
        else
            _logger.LogInformation(
                "Yarpa Agent service started (every {IntervalDays} day(s), night window {Start:00}:00-{End:00}:00 local, runOnStart={RunOnStart})",
                _serviceOptions.IntervalDays, _serviceOptions.PreferredHourStart,
                _serviceOptions.PreferredHourEnd, _serviceOptions.RunImmediatelyOnStart);

        if (_serviceOptions.RunImmediatelyOnStart)
            await RunCycleAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = ComputeDelayUntilNextRun(DateTime.Now);
            _logger.LogInformation(
                "next collection scheduled in {Hours:F1}h (at {NextRun:yyyy-MM-dd HH:mm} local)",
                delay.TotalHours, DateTime.Now.Add(delay));

            if (!await DelayAsync(delay, stoppingToken))
                return;

            await RunCycleAsync(stoppingToken);
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

    /// <summary>
    /// Computes how long to wait before the next collection.
    /// Fixed-interval override (IntervalHours &gt; 0) takes precedence; otherwise the next run
    /// is scheduled IntervalDays ahead, at a random time inside the preferred night window
    /// (local time) to spread load across machines.
    /// </summary>
    private TimeSpan ComputeDelayUntilNextRun(DateTime nowLocal)
    {
        if (_serviceOptions.IntervalHours > 0)
            return TimeSpan.FromHours(_serviceOptions.IntervalHours);

        int intervalDays = _serviceOptions.IntervalDays > 0 ? _serviceOptions.IntervalDays : 7;

        // Normalise the night window to a valid [start, end) range within 0-23.
        int startHour = Math.Clamp(_serviceOptions.PreferredHourStart, 0, 23);
        int endHour = Math.Clamp(_serviceOptions.PreferredHourEnd, 0, 24);
        if (endHour <= startHour)
            endHour = startHour + 1;

        int windowMinutes = (endHour - startHour) * 60;
        int offsetMinutes = _random.Next(windowMinutes);

        DateTime targetDay = nowLocal.Date.AddDays(intervalDays);
        DateTime nextRun = targetDay.AddHours(startHour).AddMinutes(offsetMinutes);

        // Safety: never schedule in the past.
        if (nextRun <= nowLocal)
            nextRun = nowLocal.AddDays(intervalDays);

        return nextRun - nowLocal;
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
