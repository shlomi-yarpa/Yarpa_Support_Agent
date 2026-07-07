using Microsoft.Extensions.Options;

namespace Yarpa.Api.Services.Retention;

/// <summary>
/// Periodic background runner for <see cref="IRetentionService"/>. Runs one pass shortly
/// after startup and then every <see cref="RetentionOptions.ScanIntervalHours"/>. Each run
/// is isolated in its own DI scope and wrapped in try/catch so a failure never crashes the
/// host. When retention is disabled the run is a cheap no-op.
/// </summary>
public sealed class RetentionHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionHostedService> _logger;

    public RetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionOptions> options,
        ILogger<RetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        double hours = _options.ScanIntervalHours > 0 ? _options.ScanIntervalHours : 24;
        var interval = TimeSpan.FromHours(hours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IRetentionService>();
                await service.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "retention background run failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
