using Microsoft.AspNetCore.Components;
using Yarpa.Dashboard.Models;
using Yarpa.Dashboard.Services;

namespace Yarpa.Dashboard.Pages;

/// <summary>
/// Code-behind for the machine detail page.
/// All logic and computed properties live here; the .razor file is a pure template.
/// </summary>
public partial class MachineDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter] public string MachineId { get; set; } = string.Empty;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _loading = true;
    private string? _error;
    private string _activeTab = "summary";

    private MachineSummary? _summary;

    private ChangesPage? _changesPage;
    private bool _changesLoading;
    private int _changesCurrentPage = 1;
    private const int ChangesPageSize = 50;

    private AlertsPage? _alertsPage;
    private bool _alertsLoading;
    private string _alertState = "all";
    private int _alertsCurrentPage = 1;
    private const int AlertsPageSize = 50;

    private bool   _rawSnapshotVisible;
    private bool   _rawSnapshotLoading;
    private string? _rawSnapshot;

    private bool _softwareExpanded;

    // ── Computed CSS class strings (avoids nested quotes in template) ──────────
    internal string SummaryTabCss    => _activeTab == "summary"   ? "nav-link active" : "nav-link";
    internal string TimelineTabCss   => _activeTab == "timeline"  ? "nav-link active" : "nav-link";
    internal string AlertsTabCss     => _activeTab == "alerts"    ? "nav-link active" : "nav-link";
    internal string EventLogsTabCss  => _activeTab == "eventlogs" ? "nav-link active" : "nav-link";

    internal string OpenBtnCss       => _alertState == "open"     ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary";
    internal string ResolvedBtnCss   => _alertState == "resolved" ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary";
    internal string AllBtnCss        => _alertState == "all"      ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary";

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        try
        {
            _summary = await Api.GetSummaryAsync(MachineId);
            if (_summary == null)
                _error = $"מחשב '{MachineId}' לא נמצא.";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }

        _ = LoadChanges();
        _ = LoadAlerts();
    }

    // ── Tab switching ─────────────────────────────────────────────────────────
    internal void SetTab(string tab) => _activeTab = tab;

    // ── Changes ───────────────────────────────────────────────────────────────
    private async Task LoadChanges()
    {
        _changesLoading = true;
        StateHasChanged();
        try
        {
            _changesPage = await Api.GetChangesAsync(MachineId, _changesCurrentPage, ChangesPageSize);
        }
        catch { /* non-critical */ }
        finally
        {
            _changesLoading = false;
            StateHasChanged();
        }
    }

    internal async Task ChangeChangesPage(int page)
    {
        _changesCurrentPage = page;
        await LoadChanges();
    }

    // ── Alerts ────────────────────────────────────────────────────────────────
    private async Task LoadAlerts()
    {
        _alertsLoading = true;
        StateHasChanged();
        try
        {
            _alertsPage = await Api.GetAlertsAsync(MachineId, _alertState, _alertsCurrentPage, AlertsPageSize);
        }
        catch { /* non-critical */ }
        finally
        {
            _alertsLoading = false;
            StateHasChanged();
        }
    }

    internal async Task SetAlertState(string state)
    {
        _alertState = state;
        _alertsCurrentPage = 1;
        await LoadAlerts();
    }

    internal async Task ChangeAlertsPage(int page)
    {
        _alertsCurrentPage = page;
        await LoadAlerts();
    }

    // ── Raw snapshot modal ────────────────────────────────────────────────────
    internal async Task OpenSnapshot(Guid snapshotId)
    {
        _rawSnapshotVisible = true;
        _rawSnapshotLoading = true;
        _rawSnapshot = null;
        StateHasChanged();
        try
        {
            _rawSnapshot = await Api.GetRawSnapshotAsync(snapshotId);
        }
        catch (Exception ex)
        {
            _rawSnapshot = $"שגיאה: {ex.Message}";
        }
        finally
        {
            _rawSnapshotLoading = false;
            StateHasChanged();
        }
    }

    internal void CloseSnapshot()
    {
        _rawSnapshotVisible = false;
        _rawSnapshot        = null;
    }

    internal void ToggleSoftware() => _softwareExpanded = !_softwareExpanded;

    // ── Display helpers ───────────────────────────────────────────────────────
    internal static string FormatRam(long? mb)
    {
        if (!mb.HasValue) return "—";
        return mb.Value >= 1024 ? $"{mb.Value / 1024} GB" : $"{mb.Value} MB";
    }

    internal static string FormatGb(double? gb)
        => gb.HasValue ? $"{gb.Value:F1} GB" : "—";

    internal static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays} ימים, {ts.Hours:D2}:{ts.Minutes:D2}";
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    internal static string DiskBarColor(double freePercent)
    {
        if (freePercent < 5)  return "bg-danger";
        if (freePercent < 15) return "bg-warning";
        return "bg-success";
    }

    internal static string DiskUsedWidthStyle(double freePercent)
        => $"width: {(100.0 - freePercent):F1}%";

    internal static string EventLogLevelCss(string? level) => level?.ToLowerInvariant() switch
    {
        "error"       => "badge bg-danger",
        "critical"    => "badge bg-danger",
        "warning"     => "badge bg-warning text-dark",
        "information" => "badge bg-info text-dark",
        _             => "badge bg-secondary"
    };

    internal static string Truncate(string? v, int maxLen = 120)
        => v is not null && v.Length > maxLen ? v[..maxLen] + "…" : (v ?? string.Empty);

    internal static string ChangeTypeLabel(string t) => t switch
    {
        "DeviceAdded"            => "התקן נוסף",
        "DeviceRemoved"          => "התקן הוסר",
        "ComPortChanged"         => "שינוי COM Port",
        "OsChanged"              => "שינוי מערכת הפעלה",
        "SqlChanged"             => "שינוי SQL Server",
        "PrinterChanged"         => "שינוי מדפסת",
        "SoftwareVersionChanged" => "שינוי גרסת תוכנה",
        "RamChanged"             => "שינוי RAM",
        "DiskChanged"            => "שינוי דיסק",
        "ServiceStateChanged"    => "שינוי סטטוס שירות",
        "NetworkChanged"         => "שינוי רשת",
        _                        => t
    };

    internal static string AlertTypeLabel(string t) => t switch
    {
        "ServiceDown"            => "שירות לא פעיל",
        "DiskAlmostFull"         => "דיסק כמעט מלא",
        "PaymentTerminalMissing" => "מסוף סליקה חסר",
        "SqlNotRunning"          => "SQL לא רץ",
        "OldSoftwareVersion"     => "גרסה ישנה",
        "NoRecentContact"        => "אין תקשורת",
        "CollectorError"         => "שגיאת איסוף",
        _                        => t
    };

    internal static string CollectedAtText(MachineSummary s)
        => s.CollectedAtUtc.HasValue
            ? s.CollectedAtUtc.Value.ToString("dd/MM/yyyy HH:mm:ss")
            : "—";

    internal static string ResolvedAtText(AlertItem a)
        => a.ResolvedAtUtc.HasValue
            ? a.ResolvedAtUtc.Value.ToString("dd/MM/yyyy HH:mm")
            : "—";
}
