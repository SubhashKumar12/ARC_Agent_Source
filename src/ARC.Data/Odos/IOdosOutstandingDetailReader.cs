using ARC.Domain.Odos;

namespace ARC.Data.Odos;

public interface IOdosOutstandingDetailReader
{
  Task<OdosOutstandingDetail?> GetByOdosKeysAsync(
    string depotCode,
    string dealerCode,
    CancellationToken cancellationToken);
}
