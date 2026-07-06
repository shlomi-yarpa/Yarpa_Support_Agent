using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Yarpa.Contracts.Sections;
using YarpaEventLogEntry = Yarpa.Contracts.Sections.EventLogEntry;

namespace Yarpa.Agent.Collectors.Collectors;

/// <summary>
/// Collects recent error/critical/warning events from Windows Event Log.
/// Default: System + Application logs, last 7 days, up to 200 events.
/// Requires no admin rights; EventLog is readable by normal users.
/// </summary>
public sealed class EventLogCollector : ICollector
{
    private readonly int _windowDays;
    private readonly int _maxEvents;
    private readonly IReadOnlyList<string> _logNames;

    public EventLogCollector(
        int windowDays = 7,
        int maxEvents = 200,
        IReadOnlyList<string>? logNames = null)
    {
        _windowDays = windowDays > 0 ? windowDays : 7;
        _maxEvents = maxEvents > 0 ? maxEvents : 200;
        _logNames = logNames != null && logNames.Count > 0
            ? logNames
            : new[] { "System", "Application" };
    }

    public string SectionName => "eventLogs";

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (entries, partialError) = await Task.Run(() => Collect(_windowDays, _maxEvents, _logNames), ct);
            sw.Stop();

            if (partialError != null)
                return CollectorResult.Partial(SectionName, entries, partialError, sw.ElapsedMilliseconds);

            return CollectorResult.Ok(SectionName, entries, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CollectorResult.Failed(SectionName, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static (List<YarpaEventLogEntry> Entries, string? PartialError) Collect(
        int windowDays, int maxEvents, IReadOnlyList<string> logNames)
    {
        var entries = new List<YarpaEventLogEntry>();
        string? partialError = null;
        DateTime cutoff = DateTime.UtcNow.AddDays(-windowDays);

        foreach (string logName in logNames)
        {
            try
            {
                string query = BuildXPathQuery(cutoff);
                using var reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, query));

                int count = 0;
                while (count < maxEvents)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record == null) break;

                    string? message = null;
                    try { message = record.FormatDescription(); }
                    catch { /* description may require missing message DLL */ }

                    if (string.IsNullOrEmpty(message))
                        message = string.Join("; ", record.Properties.Select(p => p.Value?.ToString() ?? string.Empty));

                    // Truncate very long messages
                    if (message.Length > 512)
                        message = message[..512] + "…";

                    entries.Add(new YarpaEventLogEntry
                    {
                        Log = logName,
                        Source = record.ProviderName ?? string.Empty,
                        EventId = record.Id,
                        Level = MapLevel(record.Level),
                        TimeUtc = record.TimeCreated.HasValue
                            ? new DateTimeOffset(record.TimeCreated.Value.ToUniversalTime(), TimeSpan.Zero)
                            : DateTimeOffset.UtcNow,
                        Message = message
                    });

                    count++;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                partialError = $"{logName} log: access denied ({ex.Message})";
            }
            catch (Exception ex)
            {
                partialError = $"{logName} log: {ex.Message}";
            }
        }

        // Sort newest first
        entries.Sort((a, b) => b.TimeUtc.CompareTo(a.TimeUtc));

        return (entries, partialError);
    }

    private static string BuildXPathQuery(DateTime cutoff)
    {
        // XPath query: Error (2), Critical (1), Warning (3) within the time window
        string iso = cutoff.ToString("yyyy-MM-ddTHH:mm:ss");
        return $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '{iso}']]]";
    }

    private static string MapLevel(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Information",
        _ => "Unknown"
    };
}
