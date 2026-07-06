using System.Text.Json.Serialization;

namespace Yarpa.Contracts.Sections;

/// <summary>SQL Server installation summary for the machine.</summary>
public sealed class SqlServerData
{
    [JsonPropertyName("installed")]
    public bool Installed { get; init; }

    [JsonPropertyName("instances")]
    public List<SqlInstanceInfo> Instances { get; init; } = new();
}
