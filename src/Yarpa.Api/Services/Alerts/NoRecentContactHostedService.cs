using Microsoft.Extensions.Options;

namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Minimal periodic runner for <see cref="INoRecentContactChecker"/>. Runs one scan shortly
/// after startup and then every <see cref="AlertOptions.NoRecentContactScanIntervalMinutes"/>.
/// Deliberately simple — no complex scheduling infrastructure (that is Stage 6 / roadmap).
/// </summary>
public sealed class NoRecentContactHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertOptions _options;
    private readonly ILogger<NoRecentContactHostedService> _logger;

    public NoRecentContactHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AlertOptions> options,
        ILogger<NoRecentContactHostedService> logger)
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

        int minutes = Math.Max(1, _options.NoRecentContactScanIntervalMinutes);
        var interval = TimeSpan.FromMinutes(minutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var checker = scope.ServiceProvider.GetRequiredService<INoRecentContactChecker>();
                await checker.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "no-recent-contact background scan failed");
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
