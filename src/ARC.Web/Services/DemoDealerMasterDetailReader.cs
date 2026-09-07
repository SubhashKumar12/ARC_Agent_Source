using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;

namespace ARC.Web.Services;

/// <summary>
/// Demo stub for DI only. Business Chat dealer lookups are proxied to ARC.Api.
/// </summary>
public sealed class DemoDealerMasterDetailReader : IDealerMasterDetailReader
{
    public Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken) =>
        Task.FromResult<DealerMasterDetail?>(null);

    public Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken) =>
        Task.FromResult<DealerMasterDetail?>(null);
}
