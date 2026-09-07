using ARC.Domain.Readiness;

namespace ARC.Domain.Readiness;

public static class ProductionCapabilityCatalog
{
    public static IReadOnlyList<ProductionCapabilityStatus> Build() =>
    [
        Cap(ProductionCapabilityKind.DealerFacts, ProductionCapabilityGate.ReadyLocal,
            ReadinessLevel.Ready, "GetAsync via ODOS.usp_ARC_GetDealer; list methods still inline.", null),
        Cap(ProductionCapabilityKind.LedgerReconciliation, ProductionCapabilityGate.ProductionDataBlocked,
            ReadinessLevel.BlockedExternal, "A1 formula local; LedgerRepository inline; ODOS opening unbound to A1.", "DEC-O04"),
        Cap(ProductionCapabilityKind.RecoverabilityRanking, ProductionCapabilityGate.BusinessDecisionBlocked,
            ReadinessLevel.Interim, "Interim net_recoverable_exposure.v1 — not assignment SP3 composite.", "DEC-M01"),
        Cap(ProductionCapabilityKind.NoticeDecision, ProductionCapabilityGate.BusinessDecisionBlocked,
            ReadinessLevel.Tbc, "R1/R5 deterministic locally; R1b denominator and PTP grace TBC.", "DEC-F01"),
        Cap(ProductionCapabilityKind.Section138Eligibility, ProductionCapabilityGate.LegalDecisionBlocked,
            ReadinessLevel.Tbc, "R2 local; return memo/ValidityEnd/qualifying codes production-blocked.", "DEC-L04"),
        Cap(ProductionCapabilityKind.LegalClock, ProductionCapabilityGate.LegalDecisionBlocked,
            ReadinessLevel.Tbc, "Clock service local; 30/15/30 LEGAL_TBC; served date SoR missing.", "DEC-L01"),
        Cap(ProductionCapabilityKind.EvidenceCaseFile, ProductionCapabilityGate.ReadyLocal,
            ReadinessLevel.Ready, "A7 bundle + ARC completeness MERGE; Blob/WORM Azure pending.", null),
        Cap(ProductionCapabilityKind.PtpField, ProductionCapabilityGate.AzureVerificationPending,
            ReadinessLevel.AzurePending, "SQL SP-backed PTP/chase/visit; Azure Speech/timer unverified.", "DEC-A03"),
        Cap(ProductionCapabilityKind.Supervision, ProductionCapabilityGate.BusinessDecisionBlocked,
            ReadinessLevel.Interim, "A8 exception queue read-only; no SP9 learning.", "DEC-M09"),
        Cap(ProductionCapabilityKind.KnowledgeRetrieval, ProductionCapabilityGate.AzureVerificationPending,
            ReadinessLevel.Tbc, "Lexical/vector route selection; fusion NOT_MEASURED; Cosmos vector Azure pending.", "DEC-A02"),
    ];

    private static ProductionCapabilityStatus Cap(
        ProductionCapabilityKind kind,
        ProductionCapabilityGate gate,
        ReadinessLevel level,
        string summary,
        string? decisionId)
        => new(kind, gate, level, summary, decisionId);
}
