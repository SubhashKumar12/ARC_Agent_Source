using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Cli.Fakes;

/// <summary>
/// S1–S9 scenarios use <see cref="InMemoryArcStore"/> — not corporate ODOS chat readers.
/// Stubs satisfy DI validation for tools registered via <c>AddArcTools</c>.
/// </summary>
internal sealed class CliUnusedDealerMasterDetailReader : IDealerMasterDetailReader
{
    public Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult<DealerMasterDetail?>(null);

    public Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
        => Task.FromResult<DealerMasterDetail?>(null);
}

internal sealed class CliUnusedOdosOutstandingDetailReader : IOdosOutstandingDetailReader
{
    public Task<OdosOutstandingDetail?> GetByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
        => Task.FromResult<OdosOutstandingDetail?>(null);
}
