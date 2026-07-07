using System.Text.Json;
using Yarpa.Contracts;

namespace Yarpa.Api.Services.Alerts;

/// <summary>
/// Helpers for reading section data out of a <see cref="DiagnosticsSnapshot"/> whose
/// section payloads are <see cref="JsonElement"/> values. Sections whose status is
/// <see cref="CollectorStatus.Error"/> are treated as "no data" so that a collection
/// failure is never misinterpreted as a real state (e.g. SQL not running).
/// </summary>
internal static class SectionReader
{
    /// <summary>
    /// Attempts to extract the data element from a section. Returns false when the section is
    /// missing, has Error status, or its data is not a JsonElement. Partial sections are allowed
    /// through — they still carry real (if incomplete) data.
    /// </summary>
    public static bool TryGetData(DiagnosticsSnapshot snapshot, string section, out JsonElement data)
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

    /// <summary>Returns the status of a section, or null when the section is absent.</summary>
    public static CollectorStatus? GetStatus(DiagnosticsSnapshot snapshot, string section)
    {
        if (snapshot.Sections.TryGetValue(section, out SnapshotSection? sec))
            return sec.Status;
        return null;
    }

    public static string Str(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.ValueKind != JsonValueKind.Null)
        {
            return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? string.Empty) : p.GetRawText();
        }
        return string.Empty;
    }

    public static bool TryGetDouble(JsonElement el, string prop, out double value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.ValueKind == JsonValueKind.Number
            && p.TryGetDouble(out value);
    }

    public static bool BoolProp(JsonElement el, string prop)
    {
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(prop, out JsonElement p)
            && p.ValueKind == JsonValueKind.True;
    }
}
