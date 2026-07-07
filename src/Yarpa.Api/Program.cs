using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Yarpa.Api.Data;
using Yarpa.Api.Middleware;
using Yarpa.Api.Services;
using Yarpa.Api.Services.Alerts;
using Yarpa.Api.Services.Alerts.Rules;
using Yarpa.Api.Validation;
using Yarpa.Contracts;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // ── EF Core ──────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<YarpaDbContext>(opts =>
        opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

    // ── MVC ──────────────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            // Keep camelCase names; per-type converters (e.g. CamelCaseJsonStringEnumConverter
            // on CollectorStatus) are applied at the type level and remain in effect.
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

    // ── FluentValidation ─────────────────────────────────────────────────────
    builder.Services.AddScoped<IValidator<DiagnosticsSnapshot>, DiagnosticsSnapshotValidator>();

    // ── Comparison + Alert options (configurable thresholds) ──────────────────
    builder.Services.Configure<ComparisonOptions>(
        builder.Configuration.GetSection("Comparison"));
    builder.Services.Configure<AlertOptions>(
        builder.Configuration.GetSection("Alerts"));

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddScoped<IClientResolver, ClientResolver>();
    builder.Services.AddScoped<ISnapshotComparer, SnapshotComparer>();
    builder.Services.AddScoped<ISnapshotStore, SnapshotStore>();

    // ── Alert engine + modular rules (order defines evaluation order) ──────────
    builder.Services.AddScoped<IAlertRule, ServiceDownRule>();
    builder.Services.AddScoped<IAlertRule, SqlNotRunningRule>();
    builder.Services.AddScoped<IAlertRule, DiskAlmostFullRule>();
    builder.Services.AddScoped<IAlertRule, PaymentTerminalMissingRule>();
    builder.Services.AddScoped<IAlertRule, OldSoftwareVersionRule>();
    builder.Services.AddScoped<IAlertRule, CollectorErrorRule>();
    builder.Services.AddScoped<IAlertEngine, AlertEngine>();
    builder.Services.AddScoped<INoRecentContactChecker, NoRecentContactChecker>();

    // ── Periodic no-recent-contact scan (skipped under the Testing environment) ─
    if (!builder.Environment.IsEnvironment("Testing"))
        builder.Services.AddHostedService<NoRecentContactHostedService>();

    WebApplication app = builder.Build();

    app.UseSerilogRequestLogging();

    // ── Liveness probe (no auth required) ────────────────────────────────────
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // ── ApiKey authentication middleware ──────────────────────────────────────
    app.UseMiddleware<ApiKeyMiddleware>();

    app.MapControllers();

    // ── Apply migrations on startup (dev/local convenience) ──────────────────
    // Skipped for InMemory (tests) or non-Development environments.
    if (app.Environment.IsDevelopment())
    {
        using IServiceScope scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YarpaDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
    }

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

/// <summary>Exposed so WebApplicationFactory-based integration tests can bootstrap the API.</summary>
public partial class Program { }
