namespace Yarpa.Agent;

/// <summary>
/// Strongly-typed Agent configuration bound from the "Agent" section of appsettings.json
/// (and environment variables / command line via the Generic Host). Secrets such as
/// <see cref="ApiKey"/> are supplied locally and never committed to source control.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base URL of the REST API, e.g. https://localhost:5001. HTTPS only.</summary>
    public string ApiBaseUrl { get; init; } = string.Empty;

    /// <summary>Per-customer API key sent in the X-Api-Key header. Placeholder by default.</summary>
    public string ApiKey { get; init; } = string.Empty;
}
