using ARC.Data.Odos;
using ARC.Domain.Odos;

namespace ARC.Web.Services;

/// <summary>
/// ARC.Web proxies business chat to ARC.Api; this stub satisfies AddArcTools DI only.
/// </summary>
internal sealed class WebUnusedOutstandingDetailReader : IOdosOutstandingDetailReader
{
    public Task<OdosOutstandingDetail?> GetByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("Outstanding reads are handled by ARC.Api in Business Chat.");
}
