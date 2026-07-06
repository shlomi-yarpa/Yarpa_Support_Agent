using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using System.Text.Json;
using Yarpa.Agent;
using Yarpa.Agent.Collectors;
using Yarpa.Agent.Collectors.Collectors;
using Yarpa.Contracts.Sections;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    CliArgs cli = CliArgs.Parse(args);

    using IHost host = Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services))
        .ConfigureServices((context, services) =>
        {
            services.AddOptions<AgentOptions>()
                .Bind(context.Configuration.GetSection(AgentOptions.SectionName));

            services.AddSingleton(cli);
            services.AddSingleton<MachineIdentity>();
            services.AddSingleton<OfflineQueue>();
            services.AddSingleton<SnapshotSender>();
            services.AddSingleton<CollectionOrchestrator>();

            // ── Collector options ────────────────────────────────────────────────
            var yarpaDetectionOpts = context.Configuration
                .GetSection(YarpaDetectionOptions.SectionName)
                .Get<YarpaDetectionOptions>() ?? new YarpaDetectionOptions();

            var collectorsConfig = context.Configuration.GetSection("Collectors");

            var servicesWatchlist = collectorsConfig
                .GetSection("ServicesWatchlist")
                .Get<List<string>>();

            var softwareFilters = collectorsConfig
                .GetSection("SoftwareFilters")
                .Get<List<string>>();

            var eventLogDays = collectorsConfig.GetValue<int>("EventLogWindowDays");
            var eventLogMax = collectorsConfig.GetValue<int>("EventLogMaxEvents");
            var eventLogSources = collectorsConfig
                .GetSection("EventLogSources")
                .Get<List<string>>();

            // Register collectors — add/remove via DI only, no orchestrator changes needed
            services.AddTransient<ICollector, SystemInfoCollector>();
            services.AddTransient<ICollector, OperatingSystemCollector>();
            services.AddTransient<ICollector, HardwareCollector>();
            services.AddTransient<ICollector, DiskCollector>();
            services.AddTransient<ICollector, NetworkCollector>();
            services.AddTransient<ICollector, PrintersCollector>();
            services.AddTransient<ICollector, UsbDevicesCollector>();
            services.AddTransient<ICollector, ComPortsCollector>();
            services.AddTransient<ICollector>(_ =>
                new PaymentTerminalsCollector());
            services.AddTransient<ICollector>(_ =>
                new WindowsServicesCollector(servicesWatchlist?.AsReadOnly()));
            services.AddTransient<ICollector, SqlServerCollector>();
            services.AddTransient<ICollector>(_ =>
                new InstalledSoftwareCollector(softwareFilters?.AsReadOnly()));
            services.AddTransient<ICollector>(_ =>
                new EventLogCollector(
                    windowDays: eventLogDays > 0 ? eventLogDays : 7,
                    maxEvents: eventLogMax > 0 ? eventLogMax : 200,
                    logNames: eventLogSources?.AsReadOnly()));
            services.AddTransient<ICollector>(_ =>
                new YarpaVersionCollector(yarpaDetectionOpts));

            // Named HttpClient "YarpaApi" with Polly retry (exponential backoff)
            AgentOptions agentOptions = context.Configuration
                .GetSection(AgentOptions.SectionName)
                .Get<AgentOptions>() ?? new AgentOptions();

            services
                .AddHttpClient(SnapshotSender.HttpClientName, client =>
                {
                    if (!string.IsNullOrWhiteSpace(agentOptions.ApiBaseUrl))
                        client.BaseAddress = new Uri(agentOptions.ApiBaseUrl);

                    client.DefaultRequestHeaders.Add("X-Api-Key", agentOptions.ApiKey);
                    client.Timeout = TimeSpan.FromSeconds(30);
                })
                .AddPolicyHandler(BuildRetryPolicy(agentOptions));
        })
        .Build();

    await host.StartAsync();

    var logger = host.Services.GetRequiredService<ILogger<AgentApp>>();
    var orchestrator = host.Services.GetRequiredService<CollectionOrchestrator>();
    var sender = host.Services.GetRequiredService<SnapshotSender>();
    var options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    logger.LogInformation(
        "Yarpa Agent starting (mode={Mode}, apiBaseUrl={ApiBaseUrl})",
        cli.DryRun ? "dry-run" : cli.OutputPath != null ? "output" : "once",
        string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "<unset>" : options.ApiBaseUrl);

    // First: attempt to drain any snapshots waiting in the offline queue
    if (!cli.DryRun && cli.OutputPath == null)
        await sender.DrainOfflineQueueAsync(cts.Token);

    // Collect a fresh snapshot
    var snapshot = await orchestrator.CollectAsync(cts.Token);

    if (cli.DryRun)
    {
        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("dry-run snapshot:\n{Json}", json);
    }
    else if (cli.OutputPath != null)
    {
        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(cli.OutputPath, json, cts.Token);
        logger.LogInformation("snapshot written to {OutputPath}", cli.OutputPath);
    }
    else
    {
        await sender.SendAsync(snapshot, cts.Token);
    }

    await host.StopAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "agent terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy(AgentOptions options)
{
    int retryCount = options.RetryCount > 0 ? options.RetryCount : 3;
    int baseDelay = options.RetryBaseDelaySeconds > 0 ? options.RetryBaseDelaySeconds : 2;

    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount,
            attempt => TimeSpan.FromSeconds(Math.Pow(baseDelay, attempt)),
            onRetry: (outcome, timeSpan, attempt, _) =>
            {
                Log.Warning(
                    "retry {Attempt}/{Max} after {Delay:F1}s (reason: {Reason})",
                    attempt, retryCount, timeSpan.TotalSeconds,
                    outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
            });
}

/// <summary>Marker type used as the logging category for the Agent application.</summary>
public sealed class AgentApp { }
