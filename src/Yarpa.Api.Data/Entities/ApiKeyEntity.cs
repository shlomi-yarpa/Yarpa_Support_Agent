namespace Yarpa.Api.Data.Entities;

/// <summary>
/// An API key belonging to a customer. The raw key value is never stored;
/// only its SHA-256 hex digest is kept in KeyHash.
/// </summary>
public sealed class ApiKeyEntity
{
    public Guid ApiKeyId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the raw API key value.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the key is revoked; null while active.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    // Navigation
    public CustomerEntity Customer { get; set; } = null!;
}
