namespace ARC.Data.Synthetic;

/// <summary>
/// Marks types and data that exist only for management-assignment evaluation.
/// These MUST NOT be treated as Berger / ODOS production tables.
/// </summary>
public static class SyntheticAssignmentLabels
{
    public const string Marker = "Synthetic / Assignment Evaluation Only";
    public const string SourceSystem = "SYNTHETIC_ASSIGNMENT";
    public const string OpeningTable = "synthetic_odos_opening";
    public const string AdjustmentTable = "synthetic_assignment_adjustment";
    public const string RecoveryTable = "synthetic_recovery_case";
    public const string ChequeTable = "synthetic_security_cheque";
    public const string MemoTable = "synthetic_return_memo";
    public const string DisputeTable = "synthetic_dispute";
    public const string MoratoriumTable = "synthetic_moratorium";
    public const string PaymentHistoryTable = "synthetic_payment_history";
    public const string PtpTable = "synthetic_ptp";
    public const string VisitTable = "synthetic_field_visit";
    public const string EvidenceTable = "synthetic_evidence_case_file";
    public const string DemandNoticeTable = "synthetic_demand_notice";
    public const string Section138Table = "synthetic_section138_facts";
}
