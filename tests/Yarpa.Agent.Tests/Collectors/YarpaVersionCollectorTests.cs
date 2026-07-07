using Yarpa.Agent.Collectors;
using Yarpa.Agent.Collectors.Collectors;
using Yarpa.Contracts;
using Yarpa.Contracts.Sections;

namespace Yarpa.Agent.Tests.Collectors;

/// <summary>
/// Tests for Piryon version detection: parsing the piryons.ini "pexe=" line, deriving the
/// build number, and end-to-end detection from an explicit psoftw directory.
/// </summary>
public class YarpaVersionCollectorTests
{
    // ── pexe= parsing ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\psoftw\piryon2.exe.1.0.898.10235", "1.0.898.10235", 10235)]
    [InlineData(@"C:\psoftw\piryons.exe.1.0.900.10400", "1.0.900.10400", 10400)]
    [InlineData(@"\psoftw\piryon2.exe.2.1.0.99", "2.1.0.99", 99)]
    public void TryParseVersionFromPexe_WithExeDotVersion_ParsesVersionAndBuild(
        string pexe, string expectedVersion, int expectedBuild)
    {
        bool ok = YarpaVersionCollector.TryParseVersionFromPexe(pexe, out string version, out int? build);

        Assert.True(ok);
        Assert.Equal(expectedVersion, version);
        Assert.Equal(expectedBuild, build);
    }

    [Fact]
    public void TryParseVersionFromPexe_TrailingVersionWithoutExeMarker_Parses()
    {
        bool ok = YarpaVersionCollector.TryParseVersionFromPexe(
            @"\psoftw\somebinary 1.0.898.10235", out string version, out int? build);

        Assert.True(ok);
        Assert.Equal("1.0.898.10235", version);
        Assert.Equal(10235, build);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\psoftw\piryon2.exe")]     // no version at all
    [InlineData("notaversion")]
    public void TryParseVersionFromPexe_NoVersion_ReturnsFalse(string pexe)
    {
        bool ok = YarpaVersionCollector.TryParseVersionFromPexe(pexe, out _, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("1.0.898.10235", 10235)]
    [InlineData("8.4.2", 2)]
    [InlineData("10300", 10300)]
    [InlineData(null, null)]
    [InlineData("1.0.x", null)]
    public void ParseBuild_ReturnsLastNumericSegment(string? version, int? expected)
    {
        Assert.Equal(expected, YarpaVersionCollector.ParseBuild(version));
    }

    // ── End-to-end detection from an explicit psoftw folder ───────────────────

    [Fact]
    public async Task CollectAsync_IniFilePresent_DetectsVersionAndBuild()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "yarpa-test-" + Guid.NewGuid().ToString("N"));
        string psoftw = Path.Combine(tempRoot, "psoftw");
        Directory.CreateDirectory(psoftw);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(psoftw, "piryons.ini"),
                "[Main]\r\nsomekey=foo\r\npexe=\\psoftw\\piryon2.exe.1.0.898.10235\r\nother=bar\r\n");

            var options = new YarpaDetectionOptions { ExplicitPsoftwPaths = [psoftw] };
            var collector = new YarpaVersionCollector(options);

            CollectorResult result = await collector.CollectAsync(CancellationToken.None);

            Assert.Equal(CollectorStatus.Ok, result.Status);
            var data = Assert.IsType<YarpaVersionData>(result.Data);
            Assert.Equal("Piryon", data.Product);
            Assert.Equal("1.0.898.10235", data.Version);
            Assert.Equal(10235, data.Build);
            Assert.Equal("iniFile", data.DetectedBy);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CollectAsync_NothingFound_ReturnsOkNotFound()
    {
        string missing = Path.Combine(Path.GetTempPath(), "yarpa-missing-" + Guid.NewGuid().ToString("N"), "psoftw");
        var options = new YarpaDetectionOptions
        {
            ExplicitPsoftwPaths = [missing],
            // Prevent picking up a real psoftw folder on the test host's fixed drives.
            PsoftwFolderName = "psoftw-nonexistent-" + Guid.NewGuid().ToString("N")
        };

        CollectorResult result = await new YarpaVersionCollector(options).CollectAsync(CancellationToken.None);

        Assert.Equal(CollectorStatus.Ok, result.Status);
        var data = Assert.IsType<YarpaVersionData>(result.Data);
        Assert.Equal("Piryon", data.Product);
        Assert.Equal("notFound", data.DetectedBy);
        Assert.Null(data.Version);
    }
}
