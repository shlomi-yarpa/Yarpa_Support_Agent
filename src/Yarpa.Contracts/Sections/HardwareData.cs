using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>BIOS identification data.</summary>
public sealed class BiosInfo
{
    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Release date as an ISO-8601 date string (yyyy-MM-dd) when available.</summary>
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; init; }
}

/// <summary>CPU identification data.</summary>
public sealed class CpuInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("cores")]
    public int Cores { get; init; }

    /// <summary>Logical (hyper-threaded) processor count.</summary>
    [JsonPropertyName("logical")]
    public int Logical { get; init; }
}

/// <summary>
/// Payload for the "hardware" section: motherboard, BIOS, CPU, and RAM.
/// </summary>
public sealed class HardwareData
{
    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; init; } = string.Empty;

    [JsonPropertyName("bios")]
    public BiosInfo Bios { get; init; } = new();

    [JsonPropertyName("cpu")]
    public CpuInfo Cpu { get; init; } = new();

    /// <summary>Total installed RAM in megabytes.</summary>
    [JsonPropertyName("ramTotalMb")]
    public long RamTotalMb { get; init; }

    /// <summary>Number of physical RAM modules installed.</summary>
    [JsonPropertyName("ramModules")]
    public int RamModules { get; init; }
}
