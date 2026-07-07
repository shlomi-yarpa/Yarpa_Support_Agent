using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services;

/// <summary>
/// Compares two consecutive snapshots and emits <see cref="ChangeEntity"/> records
/// for every meaningful difference. Sections whose status is <see cref="CollectorStatus.Error"/>
/// in either snapshot are skipped so that collection failures are never misread as
/// device removals.
/// </summary>
public sealed class SnapshotComparer : ISnapshotComparer
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<SnapshotComparer> _logger;
    private readonly double _diskThreshold;

    public SnapshotComparer(
        IOptions<ComparisonOptions> options,
        ILogger<SnapshotComparer> logger)
    {
        _logger = logger;
        _diskThreshold = options.Value.DiskFreePercentChangeThreshold;
    }

    public IReadOnlyList<ChangeEntity> Compare(
        DiagnosticsSnapshot newSnapshot,
        string? previousRawJson)
    {
        if (previousRawJson is null)
            return Array.Empty<ChangeEntity>();

        DiagnosticsSnapshot? prev;
        try
        {
            prev = JsonSerializer.Deserialize<DiagnosticsSnapshot>(previousRawJson, DeserializeOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "failed to deserialize previous snapshot for machine {MachineId}; skipping comparison",
                newSnapshot.MachineId);
            return Array.Empty<ChangeEntity>();
        }

        if (prev is null)
            return Array.Empty<ChangeEntity>();

        var changes = new List<ChangeEntity>();
        var now = DateTime.UtcNow;

        CompareOs(newSnapshot, prev, changes, now);
        CompareHardware(newSnapshot, prev, changes, now);
        CompareDisks(newSnapshot, prev, changes, now);
        CompareNetwork(newSnapshot, prev, changes, now);
        ComparePrinters(newSnapshot, prev, changes, now);
        CompareUsbDevices(newSnapshot, prev, changes, now);
        ComparePaymentTerminals(newSnapshot, prev, changes, now);
        CompareComPorts(newSnapshot, prev, changes, now);
        CompareServices(newSnapshot, prev, changes, now);
        CompareSqlServer(newSnapshot, prev, changes, now);
        CompareYarpaVersion(newSnapshot, prev, changes, now);

        _logger.LogInformation(
            "snapshot {SnapshotId}: detected {Count} change(s) for machine {MachineId}",
            newSnapshot.SnapshotId, changes.Count, newSnapshot.MachineId);

        return changes;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ChangeEntity MakeChange(
        DiagnosticsSnapshot snap,
        string changeType,
        string sectionName,
        string? oldValue,
        string? newValue,
        DateTime detectedAt) => new()
        {
            MachineId = snap.MachineId,
            SnapshotId = snap.SnapshotId,
            ChangeType = changeType,
            SectionName = sectionName,
            OldValue = oldValue,
            NewValue = newValue,
            DetectedAtUtc = detectedAt
        };

    /// <summary>
    /// Attempts to extract the data element from a section.
    /// Returns false if the section is missing or has Error status (skip comparison).
    /// Partial sections are allowed through — they contain real data despite being incomplete.
    /// </summary>
    private static bool TryGetData(
        DiagnosticsSnapshot snapshot,
        string section,
        out JsonElement data)
    {
        data = default;
        if (!snapshot.Sections.TryGetValue(section, out SnapshotSection? sec))
            return false;
        if (sec.Status == CollectorStatus.Error)
            return false;
        if (sec.Data is JsonElement el)
        {
            data = el;
            return true;
        }
        return false;
    }

    private static string Str(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.ValueKind != JsonValueKind.Null)
        {
            return p.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool TryGetLong(JsonElement el, string prop, out long value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.TryGetInt64(out value);
    }

    private static bool TryGetDouble(JsonElement el, string prop, out double value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.TryGetDouble(out value);
    }

    // ── OS ────────────────────────────────────────────────────────────────────

    private void CompareOs(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "os", out JsonElement newOs)
            || !TryGetData(prev, "os", out JsonElement oldOs))
            return;

        string oldCaption = Str(oldOs, "caption");
        string oldVersion = Str(oldOs, "version");
        string oldBuild   = Str(oldOs, "build");

        string newCaption = Str(newOs, "caption");
        string newVersion = Str(newOs, "version");
        string newBuild   = Str(newOs, "build");

        if (oldCaption == newCaption && oldVersion == newVersion && oldBuild == newBuild)
            return;

        changes.Add(MakeChange(newSnap, ChangeType.OsChanged, "os",
            JsonSerializer.Serialize(new { caption = oldCaption, version = oldVersion, build = oldBuild }),
            JsonSerializer.Serialize(new { caption = newCaption, version = newVersion, build = newBuild }),
            now));
    }

    // ── Hardware / RAM ────────────────────────────────────────────────────────

    private void CompareHardware(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "hardware", out JsonElement newHw)
            || !TryGetData(prev, "hardware", out JsonElement oldHw))
            return;

        if (!TryGetLong(oldHw, "ramTotalMb", out long oldRam)
            || !TryGetLong(newHw, "ramTotalMb", out long newRam))
            return;

        if (oldRam == newRam)
            return;

        changes.Add(MakeChange(newSnap, ChangeType.RamChanged, "hardware",
            oldRam.ToString(),
            newRam.ToString(),
            now));
    }

    // ── Disks ─────────────────────────────────────────────────────────────────

    private void CompareDisks(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "disks", out JsonElement newDisks)
            || !TryGetData(prev, "disks", out JsonElement oldDisks))
            return;

        if (newDisks.ValueKind != JsonValueKind.Array
            || oldDisks.ValueKind != JsonValueKind.Array)
            return;

        var oldMap = IndexByStringProp(oldDisks, "drive");
        var newMap = IndexByStringProp(newDisks, "drive");

        // Added drives
        foreach (var (drive, newDisk) in newMap)
        {
            if (!oldMap.ContainsKey(drive))
            {
                changes.Add(MakeChange(newSnap, ChangeType.DiskChanged, "disks",
                    null,
                    newDisk.GetRawText(),
                    now));
            }
        }

        // Removed drives
        foreach (var (drive, oldDisk) in oldMap)
        {
            if (!newMap.ContainsKey(drive))
            {
                changes.Add(MakeChange(newSnap, ChangeType.DiskChanged, "disks",
                    oldDisk.GetRawText(),
                    null,
                    now));
            }
        }

        // Significant free-space change on existing drives
        foreach (var (drive, newDisk) in newMap)
        {
            if (!oldMap.TryGetValue(drive, out JsonElement oldDisk))
                continue;

            if (!TryGetDouble(oldDisk, "freePercent", out double oldFree)
                || !TryGetDouble(newDisk, "freePercent", out double newFree))
                continue;

            if (Math.Abs(newFree - oldFree) >= _diskThreshold)
            {
                changes.Add(MakeChange(newSnap, ChangeType.DiskChanged, "disks",
                    JsonSerializer.Serialize(new { drive, freePercent = oldFree }),
                    JsonSerializer.Serialize(new { drive, freePercent = newFree }),
                    now));
            }
        }
    }

    // ── Network ───────────────────────────────────────────────────────────────

    private void CompareNetwork(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "network", out JsonElement newNet)
            || !TryGetData(prev, "network", out JsonElement oldNet))
            return;

        JsonElement newAdapters = GetAdaptersArray(newNet);
        JsonElement oldAdapters = GetAdaptersArray(oldNet);

        if (newAdapters.ValueKind != JsonValueKind.Array
            || oldAdapters.ValueKind != JsonValueKind.Array)
            return;

        // Key by MAC (most stable); fall back to adapter name
        var oldMap = IndexByMacOrName(oldAdapters);
        var newMap = IndexByMacOrName(newAdapters);

        // Added adapters
        foreach (var (key, newAdapter) in newMap)
        {
            if (!oldMap.ContainsKey(key))
            {
                changes.Add(MakeChange(newSnap, ChangeType.NetworkChanged, "network",
                    null,
                    newAdapter.GetRawText(),
                    now));
                continue;
            }

            JsonElement oldAdapter = oldMap[key];

            string oldIp      = Str(oldAdapter, "ipv4");
            string newIp      = Str(newAdapter, "ipv4");
            string oldGateway = Str(oldAdapter, "gateway");
            string newGateway = Str(newAdapter, "gateway");
            string oldDns     = DnsSignature(oldAdapter);
            string newDns     = DnsSignature(newAdapter);

            if (oldIp != newIp || oldGateway != newGateway || oldDns != newDns)
            {
                changes.Add(MakeChange(newSnap, ChangeType.NetworkChanged, "network",
                    oldAdapter.GetRawText(),
                    newAdapter.GetRawText(),
                    now));
            }
        }

        // Removed adapters
        foreach (var (key, oldAdapter) in oldMap)
        {
            if (!newMap.ContainsKey(key))
            {
                changes.Add(MakeChange(newSnap, ChangeType.NetworkChanged, "network",
                    oldAdapter.GetRawText(),
                    null,
                    now));
            }
        }
    }

    /// <summary>Returns the adapters array from the network data element.
    /// Handles both {adapters:[...]} (NetworkData DTO) and a bare array (spec example).</summary>
    private static JsonElement GetAdaptersArray(JsonElement netData)
    {
        if (netData.ValueKind == JsonValueKind.Array)
            return netData;

        if (netData.ValueKind == JsonValueKind.Object
            && netData.TryGetProperty("adapters", out JsonElement adapters))
            return adapters;

        return default;
    }

    private static Dictionary<string, JsonElement> IndexByMacOrName(JsonElement array)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement el in array.EnumerateArray())
        {
            string mac  = Str(el, "mac");
            string name = Str(el, "name");
            string key  = string.IsNullOrWhiteSpace(mac) ? name : mac;
            if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                result[key] = el;
        }
        return result;
    }

    private static string DnsSignature(JsonElement adapter)
    {
        if (!adapter.TryGetProperty("dns", out JsonElement dns)
            || dns.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var servers = dns.EnumerateArray()
            .Select(d => d.GetString() ?? string.Empty)
            .OrderBy(s => s)
            .ToList();
        return string.Join(",", servers);
    }

    // ── Printers ──────────────────────────────────────────────────────────────

    private void ComparePrinters(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "printers", out JsonElement newPrinters)
            || !TryGetData(prev, "printers", out JsonElement oldPrinters))
            return;

        if (newPrinters.ValueKind != JsonValueKind.Array
            || oldPrinters.ValueKind != JsonValueKind.Array)
            return;

        var oldMap = IndexByStringProp(oldPrinters, "name", StringComparer.OrdinalIgnoreCase);
        var newMap = IndexByStringProp(newPrinters, "name", StringComparer.OrdinalIgnoreCase);

        foreach (var (name, newP) in newMap)
        {
            if (!oldMap.TryGetValue(name, out JsonElement oldP))
            {
                changes.Add(MakeChange(newSnap, ChangeType.PrinterChanged, "printers",
                    null, newP.GetRawText(), now));
                continue;
            }

            bool oldDefault = BoolProp(oldP, "isDefault");
            bool newDefault = BoolProp(newP, "isDefault");
            string oldPort  = Str(oldP, "portName");
            string newPort  = Str(newP, "portName");

            if (oldDefault != newDefault || oldPort != newPort)
            {
                changes.Add(MakeChange(newSnap, ChangeType.PrinterChanged, "printers",
                    oldP.GetRawText(), newP.GetRawText(), now));
            }
        }

        foreach (var (name, oldP) in oldMap)
        {
            if (!newMap.ContainsKey(name))
            {
                changes.Add(MakeChange(newSnap, ChangeType.PrinterChanged, "printers",
                    oldP.GetRawText(), null, now));
            }
        }
    }

    private static bool BoolProp(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    // ── USB Devices ───────────────────────────────────────────────────────────

    private void CompareUsbDevices(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "usbDevices", out JsonElement newDevs)
            || !TryGetData(prev, "usbDevices", out JsonElement oldDevs))
            return;

        if (newDevs.ValueKind != JsonValueKind.Array
            || oldDevs.ValueKind != JsonValueKind.Array)
            return;

        // Build multisets keyed by composite fingerprint
        var oldMultiset = BuildMultiset(oldDevs, UsbDeviceKey);
        var newMultiset = BuildMultiset(newDevs, UsbDeviceKey);

        // Map key → representative element for value serialisation
        var oldElements = BuildRepresentatives(oldDevs, UsbDeviceKey);
        var newElements = BuildRepresentatives(newDevs, UsbDeviceKey);

        foreach (var (key, newCount) in newMultiset)
        {
            oldMultiset.TryGetValue(key, out int oldCount);
            int added = newCount - oldCount;
            for (int i = 0; i < added; i++)
            {
                changes.Add(MakeChange(newSnap, ChangeType.DeviceAdded, "usbDevices",
                    null,
                    newElements.TryGetValue(key, out JsonElement el) ? el.GetRawText() : key,
                    now));
            }
        }

        foreach (var (key, oldCount) in oldMultiset)
        {
            newMultiset.TryGetValue(key, out int newCount);
            int removed = oldCount - newCount;
            for (int i = 0; i < removed; i++)
            {
                changes.Add(MakeChange(newSnap, ChangeType.DeviceRemoved, "usbDevices",
                    oldElements.TryGetValue(key, out JsonElement el) ? el.GetRawText() : key,
                    null,
                    now));
            }
        }
    }

    private static string UsbDeviceKey(JsonElement dev)
    {
        string name = Str(dev, "name");
        string vid  = Str(dev, "vid");
        string pid  = Str(dev, "pid");
        string cls  = Str(dev, "deviceClass");
        return $"{name}|{vid}|{pid}|{cls}".ToUpperInvariant();
    }

    // ── Payment Terminals ─────────────────────────────────────────────────────

    private void ComparePaymentTerminals(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "paymentTerminals", out JsonElement newTerms)
            || !TryGetData(prev, "paymentTerminals", out JsonElement oldTerms))
            return;

        if (newTerms.ValueKind != JsonValueKind.Array
            || oldTerms.ValueKind != JsonValueKind.Array)
            return;

        var oldMap = IndexByStringProp(oldTerms, null, StringComparer.OrdinalIgnoreCase,
            el => TerminalKey(el));
        var newMap = IndexByStringProp(newTerms, null, StringComparer.OrdinalIgnoreCase,
            el => TerminalKey(el));

        foreach (var (key, newT) in newMap)
        {
            if (!oldMap.ContainsKey(key))
            {
                changes.Add(MakeChange(newSnap, ChangeType.DeviceAdded, "paymentTerminals",
                    null, newT.GetRawText(), now));
            }
        }

        foreach (var (key, oldT) in oldMap)
        {
            if (!newMap.ContainsKey(key))
            {
                changes.Add(MakeChange(newSnap, ChangeType.DeviceRemoved, "paymentTerminals",
                    oldT.GetRawText(), null, now));
            }
        }
    }

    private static string TerminalKey(JsonElement t)
    {
        string vendor = Str(t, "vendor");
        string model  = Str(t, "model");
        string vid    = Str(t, "vid");
        string pid    = Str(t, "pid");
        return $"{vendor}|{model}|{vid}|{pid}".ToUpperInvariant();
    }

    // ── COM Ports ─────────────────────────────────────────────────────────────

    private void CompareComPorts(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "comPorts", out JsonElement newPorts)
            || !TryGetData(prev, "comPorts", out JsonElement oldPorts))
            return;

        if (newPorts.ValueKind != JsonValueKind.Array
            || oldPorts.ValueKind != JsonValueKind.Array)
            return;

        var oldMap = IndexByStringProp(oldPorts, "port", StringComparer.OrdinalIgnoreCase);
        var newMap = IndexByStringProp(newPorts, "port", StringComparer.OrdinalIgnoreCase);

        foreach (var (port, newP) in newMap)
        {
            if (!oldMap.TryGetValue(port, out JsonElement oldP))
            {
                changes.Add(MakeChange(newSnap, ChangeType.ComPortChanged, "comPorts",
                    null, newP.GetRawText(), now));
                continue;
            }

            if (Str(oldP, "deviceName") != Str(newP, "deviceName"))
            {
                changes.Add(MakeChange(newSnap, ChangeType.ComPortChanged, "comPorts",
                    oldP.GetRawText(), newP.GetRawText(), now));
            }
        }

        foreach (var (port, oldP) in oldMap)
        {
            if (!newMap.ContainsKey(port))
            {
                changes.Add(MakeChange(newSnap, ChangeType.ComPortChanged, "comPorts",
                    oldP.GetRawText(), null, now));
            }
        }
    }

    // ── Services ──────────────────────────────────────────────────────────────

    private void CompareServices(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "services", out JsonElement newSvcs)
            || !TryGetData(prev, "services", out JsonElement oldSvcs))
            return;

        if (newSvcs.ValueKind != JsonValueKind.Array
            || oldSvcs.ValueKind != JsonValueKind.Array)
            return;

        var oldMap = IndexByStringProp(oldSvcs, "name", StringComparer.Ordinal);
        var newMap = IndexByStringProp(newSvcs, "name", StringComparer.Ordinal);

        foreach (var (name, newSvc) in newMap)
        {
            if (!oldMap.TryGetValue(name, out JsonElement oldSvc))
                continue;

            string oldState = Str(oldSvc, "state");
            string newState = Str(newSvc, "state");

            if (!string.Equals(oldState, newState, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(MakeChange(newSnap, ChangeType.ServiceStateChanged, "services",
                    JsonSerializer.Serialize(new { name, state = oldState }),
                    JsonSerializer.Serialize(new { name, state = newState }),
                    now));
            }
        }
    }

    // ── SQL Server ────────────────────────────────────────────────────────────

    private void CompareSqlServer(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "sqlServer", out JsonElement newSql)
            || !TryGetData(prev, "sqlServer", out JsonElement oldSql))
            return;

        bool oldInstalled = BoolProp(oldSql, "installed");
        bool newInstalled = BoolProp(newSql, "installed");

        if (oldInstalled != newInstalled)
        {
            changes.Add(MakeChange(newSnap, ChangeType.SqlChanged, "sqlServer",
                JsonSerializer.Serialize(new { installed = oldInstalled }),
                JsonSerializer.Serialize(new { installed = newInstalled }),
                now));
            return;
        }

        // Compare instances keyed by instance name
        var oldInstances = SqlInstanceMap(oldSql);
        var newInstances = SqlInstanceMap(newSql);

        foreach (var (name, newInst) in newInstances)
        {
            if (!oldInstances.TryGetValue(name, out JsonElement oldInst))
            {
                changes.Add(MakeChange(newSnap, ChangeType.SqlChanged, "sqlServer",
                    null, newInst.GetRawText(), now));
                continue;
            }

            string oldVer   = Str(oldInst, "version");
            string newVer   = Str(newInst, "version");
            string oldState = Str(oldInst, "serviceState");
            string newState = Str(newInst, "serviceState");

            if (oldVer != newVer || !string.Equals(oldState, newState, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(MakeChange(newSnap, ChangeType.SqlChanged, "sqlServer",
                    oldInst.GetRawText(), newInst.GetRawText(), now));
            }
        }

        foreach (var (name, oldInst) in oldInstances)
        {
            if (!newInstances.ContainsKey(name))
            {
                changes.Add(MakeChange(newSnap, ChangeType.SqlChanged, "sqlServer",
                    oldInst.GetRawText(), null, now));
            }
        }
    }

    private static Dictionary<string, JsonElement> SqlInstanceMap(JsonElement sqlData)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (sqlData.ValueKind != JsonValueKind.Object) return map;
        if (!sqlData.TryGetProperty("instances", out JsonElement instances)) return map;
        if (instances.ValueKind != JsonValueKind.Array) return map;

        foreach (JsonElement inst in instances.EnumerateArray())
        {
            string name = Str(inst, "name");
            if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name))
                map[name] = inst;
        }
        return map;
    }

    // ── Yarpa Version ─────────────────────────────────────────────────────────

    private void CompareYarpaVersion(
        DiagnosticsSnapshot newSnap, DiagnosticsSnapshot prev,
        List<ChangeEntity> changes, DateTime now)
    {
        if (!TryGetData(newSnap, "yarpaVersion", out JsonElement newYv)
            || !TryGetData(prev, "yarpaVersion", out JsonElement oldYv))
            return;

        string oldVer = Str(oldYv, "version");
        string newVer = Str(newYv, "version");

        if (string.IsNullOrEmpty(oldVer) && string.IsNullOrEmpty(newVer))
            return;

        if (!string.Equals(oldVer, newVer, StringComparison.Ordinal))
        {
            changes.Add(MakeChange(newSnap, ChangeType.SoftwareVersionChanged, "yarpaVersion",
                oldVer, newVer, now));
        }
    }

    // ── Collection utilities ──────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> IndexByStringProp(
        JsonElement array,
        string? propName,
        StringComparer? comparer = null,
        Func<JsonElement, string>? keySelector = null)
    {
        comparer ??= StringComparer.Ordinal;
        var map = new Dictionary<string, JsonElement>(comparer);
        foreach (JsonElement el in array.EnumerateArray())
        {
            string key = keySelector is not null
                ? keySelector(el)
                : (propName is not null ? Str(el, propName) : string.Empty);

            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
                map[key] = el;
        }
        return map;
    }

    private static Dictionary<string, int> BuildMultiset(
        JsonElement array,
        Func<JsonElement, string> keySelector)
    {
        var multiset = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement el in array.EnumerateArray())
        {
            string key = keySelector(el);
            multiset.TryGetValue(key, out int count);
            multiset[key] = count + 1;
        }
        return multiset;
    }

    private static Dictionary<string, JsonElement> BuildRepresentatives(
        JsonElement array,
        Func<JsonElement, string> keySelector)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement el in array.EnumerateArray())
        {
            string key = keySelector(el);
            if (!map.ContainsKey(key))
                map[key] = el;
        }
        return map;
    }
}
