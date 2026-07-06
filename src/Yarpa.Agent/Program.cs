using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace Yarpa.Agent;

/// <summary>
/// Console entry point for the Yarpa Support Agent. Stage 0 wires up the Generic Host,
/// configuration and Serilog, logs that the agent started and exits successfully.
/// Collection and sending are implemented in later stages.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            using IHost host = Host.CreateDefaultBuilder(args)
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services))
                .ConfigureServices((context, services) =>
                {
                    services.AddOptions<AgentOptions>()
                        .Bind(context.Configuration.GetSection(AgentOptions.SectionName));

                    // Collectors, orchestrator and snapshot sender are registered in stage 1.
                })
                .Build();

            await host.StartAsync();

            ILogger<AgentApp> logger = host.Services.GetRequiredService<ILogger<AgentApp>>();
            AgentOptions options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

            logger.LogInformation(
                "agent started (apiBaseUrl={ApiBaseUrl})",
                string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "<unset>" : options.ApiBaseUrl);

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
    }
}

/// <summary>Marker type used as the logging category for the Agent application.</summary>
public sealed class AgentApp
{
}
