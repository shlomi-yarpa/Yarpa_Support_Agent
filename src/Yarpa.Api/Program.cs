using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    WebApplication app = builder.Build();

    app.UseSerilogRequestLogging();

    // Liveness probe. The snapshots controller and API-key middleware are added in stage 1.
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "api terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposed so the WebApplicationFactory-based integration tests can bootstrap the API.</summary>
public partial class Program
{
}
