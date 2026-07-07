using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Yarpa.Api.Data;
using Yarpa.Api.Middleware;
using Yarpa.Api.Services;
using Yarpa.Api.Services.Alerts;
using Yarpa.Api.Services.Alerts.Rules;
using Yarpa.Api.Services.Retention;
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

    // ── Comparison + Alert + Retention + Security options (configurable) ──────
    builder.Services.Configure<ComparisonOptions>(
        builder.Configuration.GetSection("Comparison"));
    builder.Services.Configure<AlertOptions>(
        builder.Configuration.GetSection("Alerts"));
    builder.Services.Configure<RetentionOptions>(
        builder.Configuration.GetSection(RetentionOptions.SectionName));
    builder.Services.Configure<SecurityOptions>(
        builder.Configuration.GetSection(SecurityOptions.SectionName));

    SecurityOptions securityOptions = builder.Configuration
        .GetSection(SecurityOptions.SectionName)
        .Get<SecurityOptions>() ?? new SecurityOptions();

    // ── Basic per-API-key rate limiting (429 on exceed) ───────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("per-key", httpContext =>
        {
            string partitionKey =
                httpContext.Request.Headers["X-Api-Key"].FirstOrDefault()
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, securityOptions.RateLimit.PermitPerWindow),
                    Window = TimeSpan.FromSeconds(Math.Max(1, securityOptions.RateLimit.WindowSeconds)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = Math.Max(0, securityOptions.RateLimit.QueueLimit)
                });
        });
    });

    // ── Kestrel body-size cap (defence in depth alongside PayloadSizeMiddleware) ─
    builder.WebHost.ConfigureKestrel(kestrel =>
        kestrel.Limits.MaxRequestBodySize = securityOptions.MaxRequestBodyBytes > 0
            ? securityOptions.MaxRequestBodyBytes
            : 5 * 1024 * 1024);

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<SnapshotMetrics>();
    builder.Services.AddScoped<IClientResolver, ClientResolver>();
    builder.Services.AddScoped<ISnapshotComparer, SnapshotComparer>();
    builder.Services.AddScoped<ISnapshotStore, SnapshotStore>();
    builder.Services.AddScoped<IRetentionService, RetentionService>();

    // ── Alert engine + modular rules (order defines evaluation order) ──────────
    builder.Services.AddScoped<IAlertRule, ServiceDownRule>();
    builder.Services.AddScoped<IAlertRule, SqlNotRunningRule>();
    builder.Services.AddScoped<IAlertRule, DiskAlmostFullRule>();
    builder.Services.AddScoped<IAlertRule, PaymentTerminalMissingRule>();
    builder.Services.AddScoped<IAlertRule, OldSoftwareVersionRule>();
    builder.Services.AddScoped<IAlertRule, CollectorErrorRule>();
    builder.Services.AddScoped<IAlertEngine, AlertEngine>();
    builder.Services.AddScoped<INoRecentContactChecker, NoRecentContactChecker>();

    // ── Periodic background services (skipped under the Testing environment) ──
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<NoRecentContactHostedService>();
        builder.Services.AddHostedService<RetentionHostedService>();
    }

    WebApplication app = builder.Build();

    // HTTPS only in real deployments. Skipped under Testing (TestServer has no HTTPS port).
    if (!app.Environment.IsEnvironment("Testing"))
    {
        if (!app.Environment.IsDevelopment())
            app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseSerilogRequestLogging();

    app.UseRateLimiter();

    // ── Operational probes / metrics (no auth — no customer data exposed) ─────
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/health/ready", async (YarpaDbContext db, CancellationToken ct) =>
    {
        bool dbOk = !db.Database.IsRelational() || await db.Database.CanConnectAsync(ct);
        return dbOk
            ? Results.Ok(new { status = "ready", database = "ok" })
            : Results.Json(new { status = "not-ready", database = "unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

    app.MapGet("/metrics", (SnapshotMetrics metrics) => Results.Ok(new
    {
        startedAtUtc = metrics.StartedAtUtc,
        uptimeSeconds = (long)(DateTime.UtcNow - metrics.StartedAtUtc).TotalSeconds,
        snapshotsReceived = metrics.Received,
        snapshotsAccepted = metrics.Accepted,
        snapshotsDuplicate = metrics.Duplicate,
        snapshotsRejected = metrics.Rejected,
        snapshotsFailed = metrics.Failed
    }));

    // ── Payload-size guard (413) then ApiKey authentication (401) ─────────────
    app.UseMiddleware<PayloadSizeMiddleware>();
    app.UseMiddleware<ApiKeyMiddleware>();

    app.MapControllers().RequireRateLimiting("per-key");

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
