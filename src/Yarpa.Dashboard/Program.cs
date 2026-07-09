using Serilog;
using Yarpa.Dashboard.Models;
using Yarpa.Dashboard.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Allow running under the Windows Service Control Manager. No-op when the process
    // is launched as a normal console application (e.g. from the command line).
    builder.Host.UseWindowsService();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // ── Blazor Server ─────────────────────────────────────────────────────────
    builder.Services.AddRazorPages();
    builder.Services.AddServerSideBlazor();

    // ── API client ────────────────────────────────────────────────────────────
    ApiSettings apiSettings = builder.Configuration
        .GetSection("ApiSettings")
        .Get<ApiSettings>()
        ?? throw new InvalidOperationException("ApiSettings section is missing from configuration.");

    builder.Services.AddSingleton(apiSettings);

    builder.Services.AddHttpClient<ApiClient>(client =>
    {
        client.BaseAddress = new Uri(apiSettings.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("X-Api-Key", apiSettings.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    // Accept self-signed certs in development
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    WebApplication app = builder.Build();

    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler("/Error");

    app.UseStaticFiles();
    app.UseRouting();

    app.MapBlazorHub();
    app.MapFallbackToPage("/_Host");

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "dashboard terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
