using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Api.Services;
using Yarpa.Api.Tests.Infrastructure;
using Yarpa.Contracts;

namespace Yarpa.Api.Tests;

/// <summary>
/// Unit tests for <see cref="SnapshotComparer"/>.
/// Each test builds two snapshot JSON strings, calls the comparer, and asserts the
/// expected <see cref="ChangeEntity"/> list.
/// </summary>
public class SnapshotComparerTests
{
    private static readonly JsonSerializerOptions SerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="DiagnosticsSnapshot"/> with the given sections.
    /// Sections are serialised and re-deserialised so that Data properties become
    /// JsonElement values (as they would be when coming from the API).
    /// </summary>
    private static DiagnosticsSnapshot BuildSnapshot(
        string machineId,
        Guid snapshotId,
        Dictionary<string, object?> sections)
    {
        var raw = new
        {
            snapshotId,
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = sections.ToDictionary(
                kv => kv.Key,
                kv => (object?)new { status = "ok", data = kv.Value })
        };

        string json = JsonSerializer.Serialize(raw, SerOptions);
        return JsonSerializer.Deserialize<DiagnosticsSnapshot>(json, DeserOptions)!;
    }

    /// <summary>Builds a snapshot where one section has error status (data = null).</summary>
    private static DiagnosticsSnapshot BuildSnapshotWithError(
        string machineId,
        Guid snapshotId,
        string errorSection,
        Dictionary<string, object?> okSections)
    {
        var allSections = new Dictionary<string, object?>();
        foreach (var kv in okSections)
        {
            allSections[kv.Key] = new { status = "ok", data = kv.Value };
        }
        allSections[errorSection] = new { status = "error", data = (object?)null, error = "collection failed" };

        var raw = new
        {
            snapshotId,
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = allSections
        };

        string json = JsonSerializer.Serialize(raw, SerOptions);
        return JsonSerializer.Deserialize<DiagnosticsSnapshot>(json, DeserOptions)!;
    }

    private static string ToRawJson(DiagnosticsSnapshot snap)
        => JsonSerializer.Serialize(snap, SerOptions);

    private static SnapshotComparer CreateComparer(double diskThreshold = 5.0)
    {
        var options = Options.Create(new ComparisonOptions
        {
            DiskFreePercentChangeThreshold = diskThreshold
        });
        return new SnapshotComparer(options, NullLogger<SnapshotComparer>.Instance);
    }

    // ── Baseline ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_NullPrevious_ReturnsEmpty()
    {
        var snap = BuildSnapshot("m1", Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
        });

        var comparer = CreateComparer();
        IReadOnlyList<ChangeEntity> changes = comparer.Compare(snap, null);

        Assert.Empty(changes);
    }

    // ── OS ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_OsBuildChanged_EmitsOsChanged()
    {
        var machineId = "m-os";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22000", build = "22000" }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
        });

        var comparer = CreateComparer();
        var changes = comparer.Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.OsChanged, changes[0].ChangeType);
        Assert.Equal("os", changes[0].SectionName);
        Assert.Contains("22000", changes[0].OldValue);
        Assert.Contains("22631", changes[0].NewValue);
    }

    [Fact]
    public void Compare_OsUnchanged_NoChanges()
    {
        var machineId = "m-os-same";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));
        Assert.Empty(changes);
    }

    // ── RAM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_RamChanged_EmitsRamChanged()
    {
        var machineId = "m-ram";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["hardware"] = new { ramTotalMb = 8192, ramModules = 1 }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["hardware"] = new { ramTotalMb = 16384, ramModules = 2 }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.RamChanged, changes[0].ChangeType);
        Assert.Equal("8192", changes[0].OldValue);
        Assert.Equal("16384", changes[0].NewValue);
    }

    // ── Disks ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_DiskFreeDropAboveThreshold_EmitsDiskChanged()
    {
        var machineId = "m-disk";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[] { new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[] { new { drive = "C:", sizeGb = 476.9, freeGb = 170.0, freePercent = 35.7 } }
        });

        var changes = CreateComparer(diskThreshold: 5.0).Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DiskChanged, changes[0].ChangeType);
        Assert.Contains("42", changes[0].OldValue);
        Assert.Contains("35.7", changes[0].NewValue);
    }

    [Fact]
    public void Compare_DiskFreeChangeBelowThreshold_NoChanges()
    {
        var machineId = "m-disk-small";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[] { new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[] { new { drive = "C:", sizeGb = 476.9, freeGb = 197.0, freePercent = 41.3 } }
        });

        var changes = CreateComparer(diskThreshold: 5.0).Compare(newSnap, ToRawJson(prev));
        Assert.Empty(changes);
    }

    [Fact]
    public void Compare_DiskAdded_EmitsDiskChanged()
    {
        var machineId = "m-disk-add";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[] { new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["disks"] = new[]
            {
                new { drive = "C:", sizeGb = 476.9, freeGb = 200.0, freePercent = 42.0 },
                new { drive = "D:", sizeGb = 1000.0, freeGb = 900.0, freePercent = 90.0 }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DiskChanged, changes[0].ChangeType);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("D:", changes[0].NewValue);
    }

    // ── Network ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_IpChanged_EmitsNetworkChanged()
    {
        var machineId = "m-net";
        var adapters_old = new { adapters = new[] { new { name = "Ethernet", mac = "AA:BB:CC:DD:EE:FF", ipv4 = "192.168.1.10", gateway = "192.168.1.1", dns = new[] { "8.8.8.8" } } } };
        var adapters_new = new { adapters = new[] { new { name = "Ethernet", mac = "AA:BB:CC:DD:EE:FF", ipv4 = "192.168.1.20", gateway = "192.168.1.1", dns = new[] { "8.8.8.8" } } } };

        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["network"] = adapters_old
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["network"] = adapters_new
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.NetworkChanged, changes[0].ChangeType);
        Assert.Contains("192.168.1.10", changes[0].OldValue);
        Assert.Contains("192.168.1.20", changes[0].NewValue);
    }

    [Fact]
    public void Compare_NetworkAdapterAdded_EmitsNetworkChanged()
    {
        var machineId = "m-net-add";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["network"] = new { adapters = new[] { new { name = "Ethernet", mac = "AA:BB:CC:DD:EE:FF", ipv4 = "10.0.0.1", gateway = "10.0.0.254", dns = new[] { "8.8.8.8" } } } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["network"] = new
            {
                adapters = new object[]
                {
                    new { name = "Ethernet", mac = "AA:BB:CC:DD:EE:FF", ipv4 = "10.0.0.1", gateway = "10.0.0.254", dns = new[] { "8.8.8.8" } },
                    new { name = "Wi-Fi",    mac = "11:22:33:44:55:66", ipv4 = "10.0.1.5",  gateway = "10.0.1.1",   dns = new[] { "1.1.1.1" } }
                }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.NetworkChanged, changes[0].ChangeType);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("Wi-Fi", changes[0].NewValue);
    }

    // ── Printers ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_PrinterAdded_EmitsPrinterChanged()
    {
        var machineId = "m-printer";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new[] { new { name = "HP LaserJet", isDefault = true, status = "Idle", portName = "USB001", driver = "HP LaserJet" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new object[]
            {
                new { name = "HP LaserJet",    isDefault = true,  status = "Idle", portName = "USB001", driver = "HP LaserJet" },
                new { name = "EPSON TM-T20",   isDefault = false, status = "Idle", portName = "USB002", driver = "EPSON TM-T20" }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.PrinterChanged, changes[0].ChangeType);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("EPSON TM-T20", changes[0].NewValue);
    }

    [Fact]
    public void Compare_DefaultPrinterChanged_EmitsPrinterChanged()
    {
        var machineId = "m-printer-default";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new[] { new { name = "HP LaserJet", isDefault = false, status = "Idle", portName = "USB001", driver = "HP" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new[] { new { name = "HP LaserJet", isDefault = true, status = "Idle", portName = "USB001", driver = "HP" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.PrinterChanged, changes[0].ChangeType);
    }

    [Fact]
    public void Compare_PrinterRemoved_EmitsPrinterChanged()
    {
        var machineId = "m-printer-rm";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new object[]
            {
                new { name = "HP LaserJet", isDefault = true,  status = "Idle", portName = "USB001", driver = "HP" },
                new { name = "Old Fax",     isDefault = false, status = "Idle", portName = "FAX:",   driver = "Fax" }
            }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["printers"] = new[] { new { name = "HP LaserJet", isDefault = true, status = "Idle", portName = "USB001", driver = "HP" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.PrinterChanged, changes[0].ChangeType);
        Assert.Contains("Old Fax", changes[0].OldValue);
        Assert.Null(changes[0].NewValue);
    }

    // ── USB Devices ───────────────────────────────────────────────────────────

    [Fact]
    public void Compare_UsbDeviceAdded_EmitsDeviceAdded()
    {
        var machineId = "m-usb";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { new { name = "USB Input Device", vid = "046D", pid = "C534", deviceClass = "HIDClass", manufacturer = "Logitech" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new object[]
            {
                new { name = "USB Input Device",   vid = "046D", pid = "C534",  deviceClass = "HIDClass", manufacturer = "Logitech" },
                new { name = "USB Serial Device",  vid = "11CA", pid = "0300",  deviceClass = "Ports",    manufacturer = "Verifone" }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DeviceAdded, changes[0].ChangeType);
        Assert.Equal("usbDevices", changes[0].SectionName);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("11CA", changes[0].NewValue);
    }

    [Fact]
    public void Compare_UsbDeviceRemoved_EmitsDeviceRemoved()
    {
        var machineId = "m-usb-rm";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new object[]
            {
                new { name = "USB Input Device",  vid = "046D", pid = "C534", deviceClass = "HIDClass", manufacturer = "Logitech" },
                new { name = "USB Serial Device", vid = "11CA", pid = "0300", deviceClass = "Ports",    manufacturer = "Verifone" }
            }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { new { name = "USB Input Device", vid = "046D", pid = "C534", deviceClass = "HIDClass", manufacturer = "Logitech" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DeviceRemoved, changes[0].ChangeType);
        Assert.Contains("11CA", changes[0].OldValue);
        Assert.Null(changes[0].NewValue);
    }

    [Fact]
    public void Compare_DuplicateUsbDevice_MultisetCountCorrect()
    {
        // Two identical devices present in old; only one in new → one DeviceRemoved
        var machineId = "m-usb-dup";
        var device = new { name = "USB Hub", vid = "05E3", pid = "0608", deviceClass = "USB", manufacturer = "Generic" };

        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { device, device }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { device }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DeviceRemoved, changes[0].ChangeType);
    }

    // ── Payment Terminals ─────────────────────────────────────────────────────

    [Fact]
    public void Compare_PaymentTerminalAdded_EmitsDeviceAdded()
    {
        var machineId = "m-pt";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["paymentTerminals"] = Array.Empty<object>()
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["paymentTerminals"] = new[] { new { vendor = "Verifone", model = "VX520", comPort = "COM3", vid = "11CA", pid = "0300" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DeviceAdded, changes[0].ChangeType);
        Assert.Equal("paymentTerminals", changes[0].SectionName);
    }

    [Fact]
    public void Compare_PaymentTerminalRemoved_EmitsDeviceRemoved()
    {
        var machineId = "m-pt-rm";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["paymentTerminals"] = new[] { new { vendor = "Verifone", model = "VX520", comPort = "COM3", vid = "11CA", pid = "0300" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["paymentTerminals"] = Array.Empty<object>()
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.DeviceRemoved, changes[0].ChangeType);
        Assert.Equal("paymentTerminals", changes[0].SectionName);
    }

    // ── COM Ports ─────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_ComPortDeviceNameChanged_EmitsComPortChanged()
    {
        var machineId = "m-com";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["comPorts"] = new[] { new { port = "COM3", deviceName = "USB Serial Device (COM3)" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["comPorts"] = new[] { new { port = "COM3", deviceName = "Verifone VX520 (COM3)" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.ComPortChanged, changes[0].ChangeType);
        Assert.Contains("USB Serial Device", changes[0].OldValue);
        Assert.Contains("Verifone VX520", changes[0].NewValue);
    }

    [Fact]
    public void Compare_ComPortAdded_EmitsComPortChanged()
    {
        var machineId = "m-com-add";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["comPorts"] = new[] { new { port = "COM1", deviceName = "Communications Port (COM1)" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["comPorts"] = new object[]
            {
                new { port = "COM1", deviceName = "Communications Port (COM1)" },
                new { port = "COM3", deviceName = "USB Serial Device (COM3)" }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.ComPortChanged, changes[0].ChangeType);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("COM3", changes[0].NewValue);
    }

    // ── Services ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_ServiceStateChanged_EmitsServiceStateChanged()
    {
        var machineId = "m-svc";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Running", startMode = "Auto" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Stopped", startMode = "Auto" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.ServiceStateChanged, changes[0].ChangeType);
        Assert.Contains("Running", changes[0].OldValue);
        Assert.Contains("Stopped", changes[0].NewValue);
    }

    [Fact]
    public void Compare_ServiceStateUnchanged_NoChanges()
    {
        var machineId = "m-svc-same";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Running", startMode = "Auto" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Running", startMode = "Auto" } }
        });

        Assert.Empty(CreateComparer().Compare(newSnap, ToRawJson(prev)));
    }

    // ── SQL Server ────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SqlVersionChanged_EmitsSqlChanged()
    {
        var machineId = "m-sql";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["sqlServer"] = new { installed = true, instances = new[] { new { name = "SQLEXPRESS", version = "14.0.1000.169", serviceState = "Running" } } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["sqlServer"] = new { installed = true, instances = new[] { new { name = "SQLEXPRESS", version = "14.0.2047.2", serviceState = "Running" } } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.SqlChanged, changes[0].ChangeType);
        Assert.Contains("14.0.1000", changes[0].OldValue);
        Assert.Contains("14.0.2047", changes[0].NewValue);
    }

    [Fact]
    public void Compare_SqlInstanceAdded_EmitsSqlChanged()
    {
        var machineId = "m-sql-add";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["sqlServer"] = new { installed = true, instances = new[] { new { name = "SQLEXPRESS", version = "14.0.1000.169", serviceState = "Running" } } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["sqlServer"] = new
            {
                installed = true,
                instances = new object[]
                {
                    new { name = "SQLEXPRESS",  version = "14.0.1000.169", serviceState = "Running" },
                    new { name = "MSSQLSERVER", version = "15.0.2000.5",   serviceState = "Running" }
                }
            }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.SqlChanged, changes[0].ChangeType);
        Assert.Null(changes[0].OldValue);
        Assert.Contains("MSSQLSERVER", changes[0].NewValue);
    }

    // ── Yarpa Version ─────────────────────────────────────────────────────────

    [Fact]
    public void Compare_YarpaVersionChanged_EmitsSoftwareVersionChanged()
    {
        var machineId = "m-yarpa";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["yarpaVersion"] = new { product = "Yarpa ERP", version = "8.4.1", detectedBy = "registry" }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["yarpaVersion"] = new { product = "Yarpa ERP", version = "8.4.2", detectedBy = "registry" }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Single(changes);
        Assert.Equal(ChangeType.SoftwareVersionChanged, changes[0].ChangeType);
        Assert.Equal("8.4.1", changes[0].OldValue);
        Assert.Equal("8.4.2", changes[0].NewValue);
    }

    // ── Error section handling ────────────────────────────────────────────────

    [Fact]
    public void Compare_ErrorSectionInNew_SkipsComparisonForThatSection()
    {
        var machineId = "m-err";
        // Previous has USB devices
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { new { name = "USB Serial Device", vid = "11CA", pid = "0300", deviceClass = "Ports", manufacturer = "Verifone" } }
        });

        // New snapshot has the usbDevices section as error (collection failed)
        var newSnap = BuildSnapshotWithError(machineId, Guid.NewGuid(), "usbDevices",
            new Dictionary<string, object?>()); // no ok sections

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        // Must not produce a DeviceRemoved — the section simply failed
        Assert.DoesNotContain(changes, c => c.ChangeType == ChangeType.DeviceRemoved);
    }

    [Fact]
    public void Compare_ErrorSectionInPrev_SkipsComparisonForThatSection()
    {
        var machineId = "m-err-prev";
        // Previous has error on usbDevices
        var prev = BuildSnapshotWithError(machineId, Guid.NewGuid(), "usbDevices",
            new Dictionary<string, object?>());

        // New has USB devices — must not produce DeviceAdded (prev error, so baseline unknown)
        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["usbDevices"] = new[] { new { name = "USB Serial Device", vid = "11CA", pid = "0300", deviceClass = "Ports", manufacturer = "Verifone" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.DoesNotContain(changes, c => c.ChangeType == ChangeType.DeviceAdded);
    }

    // ── Multiple changes in one snapshot ─────────────────────────────────────

    [Fact]
    public void Compare_MultipleChanges_AllDetected()
    {
        var machineId = "m-multi";
        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"]       = new { caption = "Windows 10 Pro", version = "10.0.19041", build = "19041" },
            ["hardware"] = new { ramTotalMb = 8192, ramModules = 1 },
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Running", startMode = "Auto" } }
        });

        var newSnap = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"]       = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" },
            ["hardware"] = new { ramTotalMb = 16384, ramModules = 2 },
            ["services"] = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = "Stopped", startMode = "Auto" } }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.OsChanged);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.RamChanged);
        Assert.Contains(changes, c => c.ChangeType == ChangeType.ServiceStateChanged);
    }

    // ── SnapshotId on all changes ─────────────────────────────────────────────

    [Fact]
    public void Compare_ChangesCarryNewSnapshotId()
    {
        var machineId = "m-sid";
        var newId = Guid.NewGuid();

        var prev = BuildSnapshot(machineId, Guid.NewGuid(), new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 10 Pro", version = "10.0.19041", build = "19041" }
        });

        var newSnap = BuildSnapshot(machineId, newId, new Dictionary<string, object?>
        {
            ["os"] = new { caption = "Windows 11 Pro", version = "10.0.22631", build = "22631" }
        });

        var changes = CreateComparer().Compare(newSnap, ToRawJson(prev));

        Assert.All(changes, c => Assert.Equal(newId, c.SnapshotId));
        Assert.All(changes, c => Assert.Equal(machineId, c.MachineId));
    }
}

// ── End-to-end integration: first snapshot → 0 changes; second → changes; idempotency ───

[Collection("API Integration Tests")]
public class ChangesEndpointTests
{
    private readonly TestApiFactory _factory;

    public ChangesEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static StringContent BuildSnapshotContent(
        string machineId,
        Guid? snapshotId = null,
        string osBuild = "22631",
        string ramMb = "16384",
        string serviceState = "Running")
    {
        var snapshot = new
        {
            snapshotId = (snapshotId ?? Guid.NewGuid()).ToString(),
            schemaVersion = "1.0",
            agentVersion = "1.0.0",
            machineId,
            collectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            sections = new
            {
                system = new
                {
                    status = "ok",
                    data = new { computerName = "TEST-PC" }
                },
                os = new
                {
                    status = "ok",
                    data = new { caption = "Windows 11 Pro", version = "10.0.22631", build = osBuild }
                },
                hardware = new
                {
                    status = "ok",
                    data = new { ramTotalMb = int.Parse(ramMb), ramModules = 2, manufacturer = "Dell", model = "OptiPlex", serialNumber = "SN", bios = new { manufacturer = "Dell", version = "1.0", releaseDate = "2024-01-01" }, cpu = new { name = "Core i5", cores = 4, logical = 8 } }
                },
                services = new
                {
                    status = "ok",
                    data = new[] { new { name = "MSSQLSERVER", displayName = "SQL Server", state = serviceState, startMode = "Auto" } }
                }
            }
        };

        string json = JsonSerializer.Serialize(snapshot);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    [Fact]
    public async Task FirstSnapshot_Returns202WithZeroChanges()
    {
        string machineId = $"e2e-machine-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        using var content = BuildSnapshotContent(machineId);
        var response = await client.PostAsync("/api/v1/snapshots", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        Assert.Equal(0, doc.GetProperty("changes").GetInt32());
    }

    [Fact]
    public async Task SecondSnapshotWithChanges_ReturnsCorrectChangeCount()
    {
        string machineId = $"e2e-machine-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        // First snapshot — baseline
        using var content1 = BuildSnapshotContent(machineId, osBuild: "22631", serviceState: "Running");
        var r1 = await client.PostAsync("/api/v1/snapshots", content1);
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);

        // Second snapshot — OS build changed + service stopped
        using var content2 = BuildSnapshotContent(machineId, osBuild: "22635", serviceState: "Stopped");
        var r2 = await client.PostAsync("/api/v1/snapshots", content2);
        Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);

        string body = await r2.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        Assert.True(doc.GetProperty("changes").GetInt32() >= 2,
            "Expected at least OsChanged + ServiceStateChanged");
    }

    [Fact]
    public async Task GetChanges_AfterTwoSnapshots_ReturnsTimeline()
    {
        string machineId = $"e2e-machine-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        using var c1 = BuildSnapshotContent(machineId, osBuild: "22631", serviceState: "Running");
        await client.PostAsync("/api/v1/snapshots", c1);

        using var c2 = BuildSnapshotContent(machineId, osBuild: "22635", serviceState: "Stopped");
        await client.PostAsync("/api/v1/snapshots", c2);

        var response = await client.GetAsync($"/api/v1/machines/{machineId}/changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        Assert.Equal(machineId, doc.GetProperty("machineId").GetString());
        int total = doc.GetProperty("totalCount").GetInt32();
        Assert.True(total >= 2, $"Expected >= 2 changes, got {total}");
    }

    [Fact]
    public async Task GetChanges_UnknownMachine_Returns404()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        var response = await client.GetAsync("/api/v1/machines/this-machine-does-not-exist/changes");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IdempotentResend_DoesNotDuplicateChanges()
    {
        string machineId = $"e2e-machine-{Guid.NewGuid():N}";
        var snapshotId1 = Guid.NewGuid();
        var snapshotId2 = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiFactory.ValidApiKey);

        // First snapshot
        using var c1 = BuildSnapshotContent(machineId, snapshotId: snapshotId1, osBuild: "22631");
        await client.PostAsync("/api/v1/snapshots", c1);

        // Second snapshot — creates 1+ changes
        using var c2a = BuildSnapshotContent(machineId, snapshotId: snapshotId2, osBuild: "22635");
        await client.PostAsync("/api/v1/snapshots", c2a);

        // Re-send same second snapshot — should be idempotent
        using var c2b = BuildSnapshotContent(machineId, snapshotId: snapshotId2, osBuild: "22635");
        var r2b = await client.PostAsync("/api/v1/snapshots", c2b);
        Assert.Equal(HttpStatusCode.OK, r2b.StatusCode);

        // Change count must not have doubled
        var changesResp = await client.GetAsync($"/api/v1/machines/{machineId}/changes");
        string body = await changesResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;
        int totalAfterReplay = doc.GetProperty("totalCount").GetInt32();

        // Same snapshot resent: totalCount must be the same as after the first send
        using var c2c = BuildSnapshotContent(machineId, snapshotId: snapshotId2, osBuild: "22635");
        await client.PostAsync("/api/v1/snapshots", c2c);

        var changesResp2 = await client.GetAsync($"/api/v1/machines/{machineId}/changes");
        string body2 = await changesResp2.Content.ReadAsStringAsync();
        var doc2 = JsonDocument.Parse(body2).RootElement;
        int totalAfterSecondReplay = doc2.GetProperty("totalCount").GetInt32();

        Assert.Equal(totalAfterReplay, totalAfterSecondReplay);
    }
}
