using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Services;

/// <summary>
/// Looks up a machine by its stable fingerprint under the authenticated customer.
/// If no record exists, creates one (auto-register) and persists it.
/// </summary>
public sealed class ClientResolver : IClientResolver
{
    private readonly YarpaDbContext _db;
    private readonly ILogger<ClientResolver> _logger;

    public ClientResolver(YarpaDbContext db, ILogger<ClientResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MachineEntity> ResolveOrRegisterAsync(
        string machineId,
        string computerName,
        string? siteCustomerCode,
        CustomerEntity customer,
        CancellationToken ct)
    {
        string? normalizedSiteCode = NormalizeSiteCustomerCode(siteCustomerCode);

        MachineEntity? machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.MachineId == machineId
                                   && m.CustomerId == customer.CustomerId, ct);

        if (machine == null)
        {
            machine = new MachineEntity
            {
                MachineId = machineId,
                CustomerId = customer.CustomerId,
                ComputerName = computerName,
                SiteCustomerCode = normalizedSiteCode,
                FirstSeenUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };

            _db.Machines.Add(machine);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "auto-registered new machine {MachineId} (name={ComputerName}, siteCode={SiteCustomerCode}) for customer {CustomerId}",
                machineId, computerName, normalizedSiteCode ?? "(none)", customer.CustomerId);
        }
        else if (normalizedSiteCode != null
                 && !string.Equals(machine.SiteCustomerCode, normalizedSiteCode, StringComparison.Ordinal))
        {
            machine.SiteCustomerCode = normalizedSiteCode;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "updated site customer code for machine {MachineId} to {SiteCustomerCode}",
                machineId, normalizedSiteCode);
        }

        return machine;
    }

    private static string? NormalizeSiteCustomerCode(string? siteCustomerCode)
    {
        if (string.IsNullOrWhiteSpace(siteCustomerCode))
            return null;

        return siteCustomerCode.Trim();
    }
}
