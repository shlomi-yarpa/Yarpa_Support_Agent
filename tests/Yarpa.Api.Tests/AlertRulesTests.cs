using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services.Alerts;
using Yarpa.Api.Services.Alerts.Rules;
using Yarpa.Contracts;

namespace Yarpa.Api.Tests;

/// <summary>
/// Unit tests for the individual <see cref="IAlertRule"/> implementations.
/// Each test builds a snapshot (and optionally changes) and asserts the rule's
/// <see cref="AlertRuleResult.Outcome"/> — condition holds ⇒ Raise, condition cleared ⇒ Clear,
/// no data / collection error ⇒ Leave.
/// </summary>
public class AlertRulesTests
{
    private static readonly JsonSerializerOptions SerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static DiagnosticsSnapshot Build(
        string machineId,
        params (string name, string status, object? data)[] sections)
    {
        var raw = new
        {
            snapshotId = Guid.NewGuid(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = sections.ToDictionary(
                s => s.name,
                s => (object?)new { status = s.status, data = s.data })
        };

        string json = JsonSerializer.Serialize(raw, SerOptions);
        return JsonSerializer.Deserialize<DiagnosticsSnapshot>(json, DeserOptions)!;
    }

    private static AlertEvaluationContext Ctx(
        DiagnosticsSnapshot snap,
        AlertOptions? options = null,
        IReadOnlyList<ChangeEntity>? changes = null) =>
        new(snap,
            new MachineEntity { MachineId = snap.MachineId },
            changes ?? Array.Empty<ChangeEntity>(),
            options ?? new AlertOptions(),
            DateTime.UtcNow);

    // ── ServiceDown ─────────────────────────────────────────────────────────────

    [Fact]
    public void ServiceDown_MonitoredServiceStopped_Raises()
    {
        var snap = Build("m1", ("services", "ok", new[]
        {
            new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Stopped", startMode = "Auto" }
        }));

        AlertRuleResult result = new ServiceDownRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Equal(AlertSeverity.Critical, result.Severity);
        Assert.Equal(AlertType.ServiceDown, result.AlertType);
    }

    [Fact]
    public void ServiceDown_MonitoredServiceRunning_Clears()
    {
        var snap = Build("m1", ("services", "ok", new[]
        {
            new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Running", startMode = "Auto" }
        }));

        AlertRuleResult result = new ServiceDownRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void ServiceDown_UnmonitoredServiceStopped_Clears()
    {
        var snap = Build("m1", ("services", "ok", new[]
        {
            new { name = "Spooler", displayName = "Print Spooler", state = "Stopped", startMode = "Auto" }
        }));

        AlertRuleResult result = new ServiceDownRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void ServiceDown_SectionError_Leaves()
    {
        var snap = Build("m1", ("services", "error", null));

        AlertRuleResult result = new ServiceDownRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }

    // ── DiskAlmostFull ──────────────────────────────────────────────────────────

    [Fact]
    public void DiskAlmostFull_BelowPercentThreshold_Raises()
    {
        var snap = Build("m1", ("disks", "ok", new[]
        {
            new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 },
            new { drive = "D:", sizeGb = 100.0, freeGb = 40.0, freePercent = 4.0 }
        }));

        AlertRuleResult result = new DiskAlmostFullRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Equal(AlertSeverity.Warning, result.Severity);
    }

    [Fact]
    public void DiskAlmostFull_BelowGbThresholdButHealthyPercent_Raises()
    {
        // Large disk: 60% free but only 3GB — the GB threshold (5GB) must still trigger.
        var snap = Build("m1", ("disks", "ok", new[]
        {
            new { drive = "C:", sizeGb = 5.0, freeGb = 3.0, freePercent = 60.0 }
        }));

        AlertRuleResult result = new DiskAlmostFullRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
    }

    [Fact]
    public void DiskAlmostFull_AllHealthy_Clears()
    {
        var snap = Build("m1", ("disks", "ok", new[]
        {
            new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 }
        }));

        AlertRuleResult result = new DiskAlmostFullRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void DiskAlmostFull_CustomThreshold_Respected()
    {
        var snap = Build("m1", ("disks", "ok", new[]
        {
            new { drive = "C:", sizeGb = 476.9, freeGb = 100.0, freePercent = 21.0 }
        }));

        var options = new AlertOptions { MinFreeDiskPercent = 25.0, MinFreeDiskGb = 5.0 };
        AlertRuleResult result = new DiskAlmostFullRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
    }

    [Fact]
    public void DiskAlmostFull_SectionError_Leaves()
    {
        var snap = Build("m1", ("disks", "error", null));

        AlertRuleResult result = new DiskAlmostFullRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }

    // ── SqlNotRunning ───────────────────────────────────────────────────────────

    [Fact]
    public void SqlNotRunning_InstalledButStopped_Raises()
    {
        var snap = Build("m1", ("sqlServer", "ok", new
        {
            installed = true,
            instances = new[]
            {
                new { name = "MSSQLSERVER", version = "15.0.2000", serviceState = "Stopped" }
            }
        }));

        AlertRuleResult result = new SqlNotRunningRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Equal(AlertSeverity.Critical, result.Severity);
    }

    [Fact]
    public void SqlNotRunning_InstalledAndRunning_Clears()
    {
        var snap = Build("m1", ("sqlServer", "ok", new
        {
            installed = true,
            instances = new[]
            {
                new { name = "MSSQLSERVER", version = "15.0.2000", serviceState = "Running" }
            }
        }));

        AlertRuleResult result = new SqlNotRunningRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void SqlNotRunning_NotInstalled_Clears()
    {
        var snap = Build("m1", ("sqlServer", "ok", new { installed = false, instances = Array.Empty<object>() }));

        AlertRuleResult result = new SqlNotRunningRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void SqlNotRunning_SectionError_Leaves()
    {
        var snap = Build("m1", ("sqlServer", "error", null));

        AlertRuleResult result = new SqlNotRunningRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }

    // ── OldSoftwareVersion ──────────────────────────────────────────────────────

    [Fact]
    public void OldSoftwareVersion_BuildBelowThreshold_RaisesWarning()
    {
        // version 1.0.898.10235 → build 10235 < 10300 ⇒ warning
        var snap = Build("m1", ("yarpaVersion", "ok",
            new { product = "Piryon", version = "1.0.898.10235", build = 10235, detectedBy = "iniFile" }));

        var options = new AlertOptions { MinSupportedYarpaBuild = 10300 };
        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Equal(AlertSeverity.Warning, result.Severity);
        Assert.Equal(AlertType.OldSoftwareVersion, result.AlertType);
        Assert.Contains("10235", result.Message);
    }

    [Fact]
    public void OldSoftwareVersion_BuildAtOrAboveThreshold_Clears()
    {
        var snap = Build("m1", ("yarpaVersion", "ok",
            new { product = "Piryon", version = "1.0.898.10300", build = 10300, detectedBy = "iniFile" }));

        var options = new AlertOptions { MinSupportedYarpaBuild = 10300 };
        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void OldSoftwareVersion_BuildParsedFromVersionWhenFieldMissing_Raises()
    {
        // No explicit "build" field: the last dotted segment (10250) must be parsed and compared.
        var snap = Build("m1", ("yarpaVersion", "ok",
            new { product = "Piryon", version = "1.0.898.10250", detectedBy = "iniFile" }));

        var options = new AlertOptions { MinSupportedYarpaBuild = 10300 };
        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
    }

    [Fact]
    public void OldSoftwareVersion_CustomThreshold_Respected()
    {
        var snap = Build("m1", ("yarpaVersion", "ok",
            new { product = "Piryon", version = "1.0.898.10500", build = 10500, detectedBy = "iniFile" }));

        var options = new AlertOptions { MinSupportedYarpaBuild = 11000 };
        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
    }

    [Fact]
    public void OldSoftwareVersion_UnknownVersion_Leaves()
    {
        var snap = Build("m1", ("yarpaVersion", "ok",
            new { product = "Piryon", version = (string?)null, detectedBy = "notFound" }));

        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }

    [Fact]
    public void OldSoftwareVersion_SectionError_Leaves()
    {
        var snap = Build("m1", ("yarpaVersion", "error", null));

        AlertRuleResult result = new OldSoftwareVersionRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }

    // ── CollectorError ──────────────────────────────────────────────────────────

    [Fact]
    public void CollectorError_CriticalSectionErrored_Raises()
    {
        var snap = Build("m1",
            ("os", "ok", new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }),
            ("sqlServer", "error", null));

        AlertRuleResult result = new CollectorErrorRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Contains("sqlServer", result.Message);
    }

    [Fact]
    public void CollectorError_NonCriticalSectionErrored_Clears()
    {
        var options = new AlertOptions { CriticalSections = new[] { "system", "os" } };
        var snap = Build("m1",
            ("os", "ok", new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }),
            ("printers", "error", null));

        AlertRuleResult result = new CollectorErrorRule().Evaluate(Ctx(snap, options));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void CollectorError_AllOk_Clears()
    {
        var snap = Build("m1",
            ("os", "ok", new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }));

        AlertRuleResult result = new CollectorErrorRule().Evaluate(Ctx(snap));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    // ── PaymentTerminalMissing ──────────────────────────────────────────────────

    [Fact]
    public void PaymentTerminalMissing_RemovalChange_Raises()
    {
        var snap = Build("m1");
        var change = new ChangeEntity
        {
            MachineId = "m1",
            SnapshotId = snap.SnapshotId,
            ChangeType = ChangeType.DeviceRemoved,
            SectionName = "paymentTerminals",
            OldValue = JsonSerializer.Serialize(new { vendor = "Verifone", model = "VX520", comPort = "COM3" }),
            NewValue = null,
            DetectedAtUtc = DateTime.UtcNow
        };

        AlertRuleResult result = new PaymentTerminalMissingRule()
            .Evaluate(Ctx(snap, changes: new[] { change }));

        Assert.Equal(AlertRuleOutcome.Raise, result.Outcome);
        Assert.Equal(AlertSeverity.Critical, result.Severity);
        Assert.Same(change, result.SourceChange);
        Assert.Contains("Verifone", result.Message);
    }

    [Fact]
    public void PaymentTerminalMissing_AdditionChange_Clears()
    {
        var snap = Build("m1");
        var change = new ChangeEntity
        {
            MachineId = "m1",
            SnapshotId = snap.SnapshotId,
            ChangeType = ChangeType.DeviceAdded,
            SectionName = "paymentTerminals",
            OldValue = null,
            NewValue = JsonSerializer.Serialize(new { vendor = "Verifone", model = "VX520" }),
            DetectedAtUtc = DateTime.UtcNow
        };

        AlertRuleResult result = new PaymentTerminalMissingRule()
            .Evaluate(Ctx(snap, changes: new[] { change }));

        Assert.Equal(AlertRuleOutcome.Clear, result.Outcome);
    }

    [Fact]
    public void PaymentTerminalMissing_NoRelevantChange_Leaves()
    {
        var snap = Build("m1");
        var change = new ChangeEntity
        {
            MachineId = "m1",
            SnapshotId = snap.SnapshotId,
            ChangeType = ChangeType.DeviceAdded,
            SectionName = "usbDevices",
            NewValue = "{}",
            DetectedAtUtc = DateTime.UtcNow
        };

        AlertRuleResult result = new PaymentTerminalMissingRule()
            .Evaluate(Ctx(snap, changes: new[] { change }));

        Assert.Equal(AlertRuleOutcome.Leave, result.Outcome);
    }
}
