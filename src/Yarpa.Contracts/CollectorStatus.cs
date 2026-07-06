using System.Text.Json.Serialization;

namespace Yarpa.Contracts;

/// <summary>
/// Status of a single collected section, serialized as lowercase
/// ("ok" / "partial" / "error") to lock the JSON contract between Agent and API.
/// Shared by the section envelope and by the Agent's collector results.
/// </summary>
[JsonConverter(typeof(CamelCaseJsonStringEnumConverter))]
public enum CollectorStatus
{
    /// <summary>Section collected successfully with complete data.</summary>
    Ok,

    /// <summary>Section collected but some data is missing (e.g. insufficient permissions).</summary>
    Partial,

    /// <summary>Section collection failed; data is null and the error message is populated.</summary>
    Error
}
