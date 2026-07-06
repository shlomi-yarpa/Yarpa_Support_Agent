namespace Yarpa.Api.Data.Entities;

/// <summary>
/// Represents a Yarpa customer organisation. API keys are scoped to a customer.
/// Every machine that sends a snapshot is associated with a customer via its API key.
/// </summary>
public sealed class CustomerEntity
{
    public Guid CustomerId { get; set; }

    /// <summary>Human-readable customer name (shown in the CRM dashboard).</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    // Navigation
    public ICollection<ApiKeyEntity> ApiKeys { get; set; } = new List<ApiKeyEntity>();
    public ICollection<MachineEntity> Machines { get; set; } = new List<MachineEntity>();
}
