using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yarpa.Contracts;

/// <summary>
/// Serializes enums as camelCase strings. Applied to contract enums so the JSON
/// wire format is locked regardless of the caller's <see cref="JsonSerializerOptions"/>.
/// </summary>
public sealed class CamelCaseJsonStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseJsonStringEnumConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
