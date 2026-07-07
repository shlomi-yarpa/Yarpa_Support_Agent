namespace Yarpa.Api.Data.Entities;

/// <summary>
/// String constants for the ChangeType discriminator column in the Changes table.
/// Each constant matches one of the change kinds defined in docs/specification.md §4.
/// </summary>
public static class ChangeType
{
    /// <summary>A USB device or payment terminal was connected (appears in the new snapshot but not the previous).</summary>
    public const string DeviceAdded = "DeviceAdded";

    /// <summary>A USB device or payment terminal was disconnected (present in the previous snapshot but not the new one).</summary>
    public const string DeviceRemoved = "DeviceRemoved";

    /// <summary>A COM port was added, removed, or its mapped device name changed.</summary>
    public const string ComPortChanged = "ComPortChanged";

    /// <summary>Windows OS caption, version or build changed.</summary>
    public const string OsChanged = "OsChanged";

    /// <summary>SQL Server installation, instance list, version or service state changed.</summary>
    public const string SqlChanged = "SqlChanged";

    /// <summary>A printer was added, removed, or its default/port status changed.</summary>
    public const string PrinterChanged = "PrinterChanged";

    /// <summary>Yarpa ERP (or a monitored component) version changed.</summary>
    public const string SoftwareVersionChanged = "SoftwareVersionChanged";

    /// <summary>Total installed RAM changed.</summary>
    public const string RamChanged = "RamChanged";

    /// <summary>A disk was added/removed or free-space dropped past the configured threshold.</summary>
    public const string DiskChanged = "DiskChanged";

    /// <summary>A monitored Windows service transitioned between running and stopped.</summary>
    public const string ServiceStateChanged = "ServiceStateChanged";

    /// <summary>A network adapter's IP address, gateway or DNS servers changed significantly.</summary>
    public const string NetworkChanged = "NetworkChanged";
}
