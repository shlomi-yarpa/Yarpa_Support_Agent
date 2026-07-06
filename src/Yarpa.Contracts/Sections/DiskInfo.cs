using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>
/// Information about a single logical disk drive.
/// The "disks" section carries a list of these.
/// </summary>
public sealed class DiskInfo
{
    /// <summary>Drive letter with colon, e.g. "C:".</summary>
    [JsonPropertyName("drive")]
    public string Drive { get; init; } = string.Empty;

    [JsonPropertyName("sizeGb")]
    public double SizeGb { get; init; }

    [JsonPropertyName("freeGb")]
    public double FreeGb { get; init; }

    /// <summary>Free space as a percentage of total size, rounded to one decimal.</summary>
    [JsonPropertyName("freePercent")]
    public double FreePercent { get; init; }

    /// <summary>"SSD", "HDD", or null when the media type cannot be determined.</summary>
    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; init; }
}
