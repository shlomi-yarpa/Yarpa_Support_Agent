using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services;

/// <summary>
/// Resolves (or auto-registers) the <see cref="MachineEntity"/> that corresponds
/// to a given <c>MachineId</c> under the authenticated customer.
/// </summary>
public interface IClientResolver
{
    /// <summary>
    /// Returns the existing machine, or creates a new one if this is the first
    /// snapshot from this machine for the customer.
    /// </summary>
    Task<MachineEntity> ResolveOrRegisterAsync(
        string machineId,
        string computerName,
        CustomerEntity customer,
        CancellationToken ct);
}
