using Yarpa.Agent.Collectors;
using Yarpa.Agent.Collectors.Collectors;
using Yarpa.Contracts;

namespace Yarpa.Agent.Tests.Collectors;

/// <summary>
/// Basic smoke tests for Stage 2 collectors: each collector must either return
/// a result with status Ok/Partial (data available) or status Error (no crash).
/// All tests run against the real machine, so we only assert on structure, not values.
/// </summary>
public class Stage2CollectorTests
{
    private static async Task<CollectorResult> RunCollector(ICollector collector)
    {
        return await collector.CollectAsync(CancellationToken.None);
    }

    private static void AssertValidResult(CollectorResult result)
    {
        Assert.NotNull(result);
        Assert.NotEmpty(result.SectionName);

        if (result.Status == CollectorStatus.Error)
        {
            Assert.NotNull(result.Error);
            Assert.Null(result.Data);
        }
        else
        {
            // Ok or Partial: data must be present
            Assert.NotNull(result.Data);
            Assert.True(result.DurationMs >= 0);
        }
    }

    [Fact]
    public async Task NetworkCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new NetworkCollector());
        AssertValidResult(result);
        Assert.Equal("network", result.SectionName);
    }

    [Fact]
    public async Task PrintersCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new PrintersCollector());
        AssertValidResult(result);
        Assert.Equal("printers", result.SectionName);
    }

    [Fact]
    public async Task UsbDevicesCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new UsbDevicesCollector());
        AssertValidResult(result);
        Assert.Equal("usbDevices", result.SectionName);
    }

    [Fact]
    public async Task ComPortsCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new ComPortsCollector());
        AssertValidResult(result);
        Assert.Equal("comPorts", result.SectionName);
    }

    [Fact]
    public async Task PaymentTerminalsCollector_MissingConfig_ReturnsPartialOrOk()
    {
        // No config file → should return Partial (missing config) but not Error/crash
        var result = await RunCollector(new PaymentTerminalsCollector(
            vendorConfigPath: "nonexistent-config.json"));

        Assert.NotNull(result);
        Assert.Equal("paymentTerminals", result.SectionName);
        // missing config causes Partial with empty list, not a full Error
        Assert.NotEqual(CollectorStatus.Error, result.Status);
    }

    [Fact]
    public async Task WindowsServicesCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new WindowsServicesCollector());
        AssertValidResult(result);
        Assert.Equal("services", result.SectionName);
    }

    [Fact]
    public async Task SqlServerCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new SqlServerCollector());
        AssertValidResult(result);
        Assert.Equal("sqlServer", result.SectionName);
    }

    [Fact]
    public async Task InstalledSoftwareCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new InstalledSoftwareCollector());
        AssertValidResult(result);
        Assert.Equal("installedSoftware", result.SectionName);
    }

    [Fact]
    public async Task EventLogCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new EventLogCollector(windowDays: 1, maxEvents: 10));
        AssertValidResult(result);
        Assert.Equal("eventLogs", result.SectionName);
    }

    [Fact]
    public async Task YarpaVersionCollector_ReturnsValidResult()
    {
        var result = await RunCollector(new YarpaVersionCollector());
        AssertValidResult(result);
        Assert.Equal("yarpaVersion", result.SectionName);
    }

    // ── WindowsServicesCollector watchlist matching unit tests ────────────────

    [Theory]
    [InlineData("MSSQLSERVER", "MSSQL*", true)]
    [InlineData("MSSQL$SQLEXPRESS", "MSSQL*", true)]
    [InlineData("W3SVC", "W3SVC", true)]
    [InlineData("w3svc", "W3SVC", true)]     // case-insensitive
    [InlineData("WSERVICE", "W3SVC", false)]
    [InlineData("YarpaAgent", "Yarpa*", true)]
    [InlineData("NotYarpa", "Yarpa*", false)]
    [InlineData("SQLAgent$SQLEXPRESS", "SQLAgent*", true)]
    public void WindowsServicesCollector_WatchlistPattern_MatchesCorrectly(
        string serviceName, string pattern, bool expected)
    {
        bool actual = WindowsServicesCollector.MatchesWatchlist(
            serviceName, new[] { pattern });

        Assert.Equal(expected, actual);
    }
}
