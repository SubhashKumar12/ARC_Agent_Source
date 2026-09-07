using ARC.Domain.Entities;
using ARC.Domain.Enums;

namespace ARC.Domain.Rules;

/// <summary>
/// Deterministic cheque selection: newest bounced cheque that has a return memo;
/// else newest cheque on file. Never left to a model.
/// </summary>
public static class ChequeSelection
{
    public static SecurityCheque? Select(IReadOnlyList<SecurityCheque> cheques, IReadOnlyList<ChequeReturnMemo> memos)
    {
        var bouncedWithMemo = cheques
            .Where(c => c.Status == ChequeStatus.Bounced
                        && memos.Any(m => string.Equals(m.ChequeNumber, c.ChequeNumber, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.DepositDate ?? DateOnly.MinValue)
            .FirstOrDefault();

        if (bouncedWithMemo is not null)
            return bouncedWithMemo;

        return cheques
            .OrderByDescending(c => c.DepositDate ?? DateOnly.MinValue)
            .FirstOrDefault();
    }
}
