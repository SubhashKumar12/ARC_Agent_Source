namespace ARC.Data.Sql.StoredProcedures;

/// <summary>
/// Centralized strongly typed stored procedure name configuration for production SQL.
/// Do NOT scatter SP name strings throughout repository implementations.
/// Only procedures represented here may be executed (allow-list enforcement).
/// </summary>
public sealed class SqlStoredProcedureNames
{
    public const string SectionName = "ArcData:Sql:StoredProcedures";

    // ===== Dealer =====
    /// <summary>ODOS dealer master read. Phase 13C: ODOS.usp_ARC_GetDealer.</summary>
    public string GetDealer { get; set; } = "ODOS.usp_ARC_GetDealer";

    /// <summary>List dealers by region (TSI isolation). REQUIRED SP CONTRACT / TBC.</summary>
    public string ListDealersByRegion { get; set; } = "dbo.usp_ListDealersByRegion";

    /// <summary>List all dealers (privileged). REQUIRED SP CONTRACT / TBC.</summary>
    public string ListAllDealers { get; set; } = "dbo.usp_ListAllDealers";

    // ===== Ledger =====
    /// <summary>Get ledger positions for dealer. REQUIRED SP CONTRACT / TBC.</summary>
    public string ListLedgerPositionsByDealer { get; set; } = "dbo.usp_ListLedgerPositionsByDealer";

    // ===== Cheque =====
    /// <summary>Get security cheques for dealer. Phase 12C: ODOS.usp_GetSubmittedChequesForDealerODOS.</summary>
    public string ListSecurityChequesByDealer { get; set; } = "ODOS.usp_GetSubmittedChequesForDealerODOS";

    /// <summary>Get cheque return memos for dealer. REQUIRED SP CONTRACT / TBC.</summary>
    public string ListChequeReturnMemosByDealer { get; set; } = "dbo.usp_ListChequeReturnMemosByDealer";

    // ===== Gate Decision =====
    /// <summary>Save gate decision (append-only). REQUIRED SP CONTRACT / TBC.</summary>
    public string SaveGateDecision { get; set; } = "dbo.usp_SaveGateDecision";

    /// <summary>List gate decisions for cycle+dealer. REQUIRED SP CONTRACT / TBC.</summary>
    public string ListGateDecisions { get; set; } = "dbo.usp_ListGateDecisions";

    // ===== Legal Case =====
    /// <summary>ODOS recovery case header. Phase 12C: ODOS.usp_GetDealerDetails.</summary>
    public string GetLegalCase { get; set; } = "ODOS.usp_GetDealerDetails";

    /// <summary>ODOS legal documents including S138 category 04. Phase 12C.</summary>
    public string GetLegalDocuments { get; set; } = "ODOS.usp_GetLegalDocumentsODOS";

    /// <summary>ODOS forward-only case status update. Phase 12C — not bound to UpsertAsync contract.</summary>
    public string UpdateOdosCaseStatus { get; set; } = "ODOS.usp_UpdateODOSCaseStatus";

    /// <summary>ODOS legal document persistence (S138 category 04). Phase 12C — not bound to UpsertAsync contract.</summary>
    public string SaveLegalDocument { get; set; } = "ODOS.usp_SaveLegalDocumentODOS";

    /// <summary>ARC case-file completeness overlay (dbo.LegalCase). UpsertAsync contract — not ODOS.</summary>
    public string UpsertLegalCase { get; set; } = "dbo.usp_UpsertLegalCase";

    // ===== Recovery Case Index =====
    /// <summary>Upsert recovery case index. REQUIRED SP CONTRACT / TBC.</summary>
    public string UpsertRecoveryCaseIndex { get; set; } = "dbo.usp_UpsertRecoveryCaseIndex";

    /// <summary>Get recovery case index. REQUIRED SP CONTRACT / TBC.</summary>
    public string GetRecoveryCaseIndex { get; set; } = "dbo.usp_GetRecoveryCaseIndex";

    /// <summary>List recovery cases by cycle. REQUIRED SP CONTRACT / TBC.</summary>
    public string ListRecoveryCasesByCycle { get; set; } = "dbo.usp_ListRecoveryCasesByCycle";

    /// <summary>Get ranked worklist. REQUIRED SP CONTRACT / TBC.</summary>
    public string GetRankedWorklist { get; set; } = "dbo.usp_GetRankedWorklist";

    // ===== Dealer Identity =====
    /// <summary>Get dealer source mapping. REQUIRED SP CONTRACT / TBC.</summary>
    public string GetDealerSourceMapping { get; set; } = "dbo.usp_GetDealerSourceMapping";

    /// <summary>Save resolved mapping. REQUIRED SP CONTRACT / TBC.</summary>
    public string SaveResolvedMapping { get; set; } = "dbo.usp_SaveResolvedMapping";

    /// <summary>Find dealer URNs by source identifier. REQUIRED SP CONTRACT / TBC.</summary>
    public string FindDealerUrnsByIdentifier { get; set; } = "dbo.usp_FindDealerUrnsByIdentifier";

    /// <summary>Find dealer URNs by alias value. REQUIRED SP CONTRACT / TBC.</summary>
    public string FindDealerUrnsByAlias { get; set; } = "dbo.usp_FindDealerUrnsByAlias";

    /// <summary>Save dealer alias. REQUIRED SP CONTRACT / TBC.</summary>
    public string SaveDealerAlias { get; set; } = "dbo.usp_SaveDealerAlias";

    // ===== SP7: PTP Record =====
    /// <summary>Upsert PTP record. Phase 11P script: SQL/ARC/PHASE_11P_ARC_OWNED_STORED_PROCEDURES.sql</summary>
    public string UpsertPtpRecord { get; set; } = "dbo.usp_UpsertPtpRecord";

    /// <summary>Get PTP record by id. Phase 11P script.</summary>
    public string GetPtpRecord { get; set; } = "dbo.usp_GetPtpRecord";

    /// <summary>List due confirmed PTP records (broken-PTP exclusion). Phase 11P script.</summary>
    public string ListPtpCandidatesByStatus { get; set; } = "dbo.usp_ListPtpCandidatesByStatus";

    /// <summary>List committed PTP records by cycle. Phase 11P script.</summary>
    public string ListPtpCandidatesByCycleDealer { get; set; } = "dbo.usp_ListPtpCandidatesByCycleDealer";

    /// <summary>List committed PTP records by dealer. Phase 11P script (distinct from cycle list).</summary>
    public string ListPtpCommittedByDealer { get; set; } = "dbo.usp_ListPtpCommittedByDealer";

    // ===== SP7: Visit Plan =====
    /// <summary>Upsert visit plan. REQUIRED SP CONTRACT / TBC.</summary>
    public string UpsertVisitPlan { get; set; } = "dbo.usp_UpsertVisitPlan";

    /// <summary>Get visit plan. REQUIRED SP CONTRACT / TBC.</summary>
    public string GetVisitPlan { get; set; } = "dbo.usp_GetVisitPlan";

    /// <summary>List visit plans by cycle+TSI. REQUIRED SP CONTRACT / TBC.</summary>
    public string ListVisitPlansByCycleTsi { get; set; } = "dbo.usp_ListVisitPlansByCycleTsi";

    // ===== SP7: PTP Chase =====
    /// <summary>Insert-if-absent PTP chase (TryInsertAsync). Phase 11P script.</summary>
    public string UpsertPtpChase { get; set; } = "dbo.usp_UpsertPtpChase";

    /// <summary>Get PTP chase by id. Phase 11P script.</summary>
    public string GetPtpChase { get; set; } = "dbo.usp_GetPtpChase";

    /// <summary>List PTP chases by cycle (current ListByCycleAsync). Phase 11P script.</summary>
    public string ListPtpChasesByCycleStatus { get; set; } = "dbo.usp_ListPtpChasesByCycleStatus";

    /// <summary>List PTP chases by status (current ListByStatusAsync). Phase 11P script.</summary>
    public string ListPtpChasesByTsiStatus { get; set; } = "dbo.usp_ListPtpChasesByTsiStatus";

    // ===== ODOS (future binding) =====
    /// <summary>
    /// Get ODOS opening data for dealer. Production reads ODOS.odos_opening_data.
    /// REQUIRED SP CONTRACT / TBC.
    /// </summary>
    public string GetOdosOpeningData { get; set; } = "ODOS.usp_GetOpeningDataForArc";

    /// <summary>
    /// Get business line limit. Wraps ODOS.fn_GetBusinessLineLimit + fn_GetDefaultBusinessLimit.
    /// REQUIRED SP CONTRACT / TBC.
    /// </summary>
    public string GetBusinessLineLimit { get; set; } = "ODOS.usp_GetBusinessLineLimit";
}
