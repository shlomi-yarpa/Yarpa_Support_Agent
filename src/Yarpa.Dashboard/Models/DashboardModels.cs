using System.Text.Json.Serialization;

namespace Yarpa.Dashboard.Models;

// ── Machines list ─────────────────────────────────────────────────────────────

public sealed class MachinesPage
{
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("page")]       public int Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int PageSize   { get; init; }
    [JsonPropertyName("items")]      public List<MachineListItem> Items { get; init; } = new();
}

public sealed class MachineListItem
{
    [JsonPropertyName("machineId")]      public string   MachineId      { get; init; } = string.Empty;
    [JsonPropertyName("computerName")]   public string   ComputerName   { get; init; } = string.Empty;
    [JsonPropertyName("firstSeenUtc")]   public DateTime FirstSeenUtc   { get; init; }
    [JsonPropertyName("lastSeenUtc")]    public DateTime LastSeenUtc    { get; init; }
    [JsonPropertyName("lastSnapshotId")] public Guid?    LastSnapshotId { get; init; }
    [JsonPropertyName("openAlertCount")] public int      OpenAlertCount { get; init; }
}

// ── Machine summary ───────────────────────────────────────────────────────────

public sealed class MachineSummary
{
    [JsonPropertyName("machineId")]        public string   MachineId      { get; init; } = string.Empty;
    [JsonPropertyName("computerName")]     public string   ComputerName   { get; init; } = string.Empty;
    [JsonPropertyName("firstSeenUtc")]     public DateTime FirstSeenUtc   { get; init; }
    [JsonPropertyName("lastSeenUtc")]      public DateTime LastSeenUtc    { get; init; }
    [JsonPropertyName("lastSnapshotId")]   public Guid?    LastSnapshotId { get; init; }
    [JsonPropertyName("collectedAtUtc")]   public DateTime? CollectedAtUtc { get; init; }
    [JsonPropertyName("openAlertCount")]   public int      OpenAlertCount { get; init; }

    [JsonPropertyName("os")]               public OsSummary?            Os               { get; init; }
    [JsonPropertyName("yarpaVersion")]     public YarpaVersionSummary?  YarpaVersion     { get; init; }
    [JsonPropertyName("hardware")]         public HardwareSummary?      Hardware         { get; init; }
    [JsonPropertyName("disks")]            public List<DiskSummary>?    Disks            { get; init; }
    [JsonPropertyName("sqlServer")]        public SqlServerSummary?     SqlServer        { get; init; }
    [JsonPropertyName("paymentTerminals")] public List<PaymentTerminalSummary>? PaymentTerminals { get; init; }
    [JsonPropertyName("printers")]         public List<PrinterSummary>? Printers         { get; init; }
    [JsonPropertyName("usbDevices")]       public List<UsbDeviceSummary>? UsbDevices     { get; init; }
}

public sealed class OsSummary
{
    [JsonPropertyName("caption")]      public string? Caption      { get; init; }
    [JsonPropertyName("version")]      public string? Version      { get; init; }
    [JsonPropertyName("build")]        public string? Build        { get; init; }
    [JsonPropertyName("edition")]      public string? Edition      { get; init; }
    [JsonPropertyName("architecture")] public string? Architecture { get; init; }
}

public sealed class YarpaVersionSummary
{
    [JsonPropertyName("product")]    public string? Product    { get; init; }
    [JsonPropertyName("version")]    public string? Version    { get; init; }
    [JsonPropertyName("detectedBy")] public string? DetectedBy { get; init; }
}

public sealed class HardwareSummary
{
    [JsonPropertyName("manufacturer")] public string? Manufacturer { get; init; }
    [JsonPropertyName("model")]        public string? Model        { get; init; }
    [JsonPropertyName("ramTotalMb")]   public long?   RamTotalMb   { get; init; }
    [JsonPropertyName("ramModules")]   public int?    RamModules   { get; init; }
}

public sealed class DiskSummary
{
    [JsonPropertyName("drive")]       public string? Drive       { get; init; }
    [JsonPropertyName("sizeGb")]      public double? SizeGb      { get; init; }
    [JsonPropertyName("freeGb")]      public double? FreeGb      { get; init; }
    [JsonPropertyName("freePercent")] public double? FreePercent { get; init; }
    [JsonPropertyName("mediaType")]   public string? MediaType   { get; init; }
}

public sealed class SqlServerSummary
{
    [JsonPropertyName("installed")]  public bool                     Installed { get; init; }
    [JsonPropertyName("instances")]  public List<SqlInstanceSummary> Instances { get; init; } = new();
}

public sealed class SqlInstanceSummary
{
    [JsonPropertyName("name")]         public string? Name         { get; init; }
    [JsonPropertyName("version")]      public string? Version      { get; init; }
    [JsonPropertyName("serviceState")] public string? ServiceState { get; init; }
}

public sealed class PaymentTerminalSummary
{
    [JsonPropertyName("vendor")]  public string? Vendor  { get; init; }
    [JsonPropertyName("model")]   public string? Model   { get; init; }
    [JsonPropertyName("comPort")] public string? ComPort { get; init; }
    [JsonPropertyName("vid")]     public string? Vid     { get; init; }
    [JsonPropertyName("pid")]     public string? Pid     { get; init; }
}

public sealed class PrinterSummary
{
    [JsonPropertyName("name")]      public string? Name      { get; init; }
    [JsonPropertyName("isDefault")] public bool    IsDefault { get; init; }
    [JsonPropertyName("status")]    public string? Status    { get; init; }
    [JsonPropertyName("portName")]  public string? PortName  { get; init; }
    [JsonPropertyName("driver")]    public string? Driver    { get; init; }
}

public sealed class UsbDeviceSummary
{
    [JsonPropertyName("name")]         public string? Name         { get; init; }
    [JsonPropertyName("vid")]          public string? Vid          { get; init; }
    [JsonPropertyName("pid")]          public string? Pid          { get; init; }
    [JsonPropertyName("deviceClass")]  public string? DeviceClass  { get; init; }
    [JsonPropertyName("manufacturer")] public string? Manufacturer { get; init; }
}

// ── Snapshots list ────────────────────────────────────────────────────────────

public sealed class SnapshotsPage
{
    [JsonPropertyName("machineId")]  public string MachineId  { get; init; } = string.Empty;
    [JsonPropertyName("totalCount")] public int    TotalCount { get; init; }
    [JsonPropertyName("page")]       public int    Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int    PageSize   { get; init; }
    [JsonPropertyName("items")]      public List<SnapshotMeta> Items { get; init; } = new();
}

public sealed class SnapshotMeta
{
    [JsonPropertyName("snapshotId")]     public Guid     SnapshotId     { get; init; }
    [JsonPropertyName("collectedAtUtc")] public DateTime CollectedAtUtc { get; init; }
    [JsonPropertyName("receivedAtUtc")]  public DateTime ReceivedAtUtc  { get; init; }
    [JsonPropertyName("agentVersion")]   public string   AgentVersion   { get; init; } = string.Empty;
    [JsonPropertyName("osCaption")]      public string?  OsCaption      { get; init; }
    [JsonPropertyName("yarpaVersion")]   public string?  YarpaVersion   { get; init; }
    [JsonPropertyName("ramTotalMb")]     public long?    RamTotalMb     { get; init; }
    [JsonPropertyName("minFreeDiskPct")] public double?  MinFreeDiskPct { get; init; }
    [JsonPropertyName("sqlInstalled")]   public bool?    SqlInstalled   { get; init; }
    [JsonPropertyName("changeCount")]    public int      ChangeCount    { get; init; }
}

// ── Changes (Timeline) ────────────────────────────────────────────────────────

public sealed class ChangesPage
{
    [JsonPropertyName("machineId")]  public string MachineId  { get; init; } = string.Empty;
    [JsonPropertyName("totalCount")] public int    TotalCount { get; init; }
    [JsonPropertyName("page")]       public int    Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int    PageSize   { get; init; }
    [JsonPropertyName("items")]      public List<ChangeItem> Items { get; init; } = new();
}

public sealed class ChangeItem
{
    [JsonPropertyName("changeId")]      public long     ChangeId      { get; init; }
    [JsonPropertyName("changeType")]    public string   ChangeType    { get; init; } = string.Empty;
    [JsonPropertyName("sectionName")]   public string   SectionName   { get; init; } = string.Empty;
    [JsonPropertyName("oldValue")]      public string?  OldValue      { get; init; }
    [JsonPropertyName("newValue")]      public string?  NewValue      { get; init; }
    [JsonPropertyName("detectedAtUtc")] public DateTime DetectedAtUtc { get; init; }
    [JsonPropertyName("snapshotId")]    public Guid     SnapshotId    { get; init; }
}

// ── Alerts ────────────────────────────────────────────────────────────────────

public sealed class AlertsPage
{
    [JsonPropertyName("machineId")]  public string MachineId  { get; init; } = string.Empty;
    [JsonPropertyName("state")]      public string State      { get; init; } = string.Empty;
    [JsonPropertyName("totalCount")] public int    TotalCount { get; init; }
    [JsonPropertyName("page")]       public int    Page       { get; init; }
    [JsonPropertyName("pageSize")]   public int    PageSize   { get; init; }
    [JsonPropertyName("items")]      public List<AlertItem> Items { get; init; } = new();
}

public sealed class AlertItem
{
    [JsonPropertyName("alertId")]          public long      AlertId          { get; init; }
    [JsonPropertyName("alertType")]        public string    AlertType        { get; init; } = string.Empty;
    [JsonPropertyName("severity")]         public string    Severity         { get; init; } = string.Empty;
    [JsonPropertyName("message")]          public string    Message          { get; init; } = string.Empty;
    [JsonPropertyName("state")]            public string    State            { get; init; } = string.Empty;
    [JsonPropertyName("createdAtUtc")]     public DateTime  CreatedAtUtc     { get; init; }
    [JsonPropertyName("resolvedAtUtc")]    public DateTime? ResolvedAtUtc    { get; init; }
    [JsonPropertyName("sourceSnapshotId")] public Guid?     SourceSnapshotId { get; init; }
    [JsonPropertyName("sourceChangeId")]   public long?     SourceChangeId   { get; init; }
}

// ── API settings ──────────────────────────────────────────────────────────────

public sealed class ApiSettings
{
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey  { get; init; } = string.Empty;
}
