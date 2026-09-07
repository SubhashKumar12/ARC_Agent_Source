namespace ARC.Domain.BusinessChat;

/// <summary>
/// Governed business-chat read capabilities. One capability may answer many natural-language phrasings.
/// </summary>
public enum BusinessCapability
{
    DealerIdentity = 1,
    DealerOutstanding = 2,
    DealerRecoveryStatus = 3,
    DealerVisitAndPtp = 4,
    DealerChequeAndLegal = 5,
    DealerEvidenceStatus = 6,
    DealerFinancialAdjustments = 7,
    DealerOdosVisitAggregate = 8,
}
