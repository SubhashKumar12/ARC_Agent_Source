using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Odos;

/// <summary>
/// Row shape for ODOS.usp_GetSubmittedChequesForDealerODOS (documented columns only).
/// </summary>
internal sealed class OdosSubmittedChequeRow
{
    public decimal confirmation_id { get; set; }
    public string? cheque_no { get; set; }
    public DateTime? cheque_date { get; set; }
    public decimal amount { get; set; }
    public DateTime? bounce_date { get; set; }
    public string? bank_name { get; set; }
    public string? branch_name { get; set; }
    public string? ifsc_code { get; set; }
    public string? submission_status { get; set; }
    public decimal? legal_id { get; set; }
    public DateTime? created_on { get; set; }
    public string? cheque_status { get; set; }
    public string? first_cheque_status { get; set; }
    public string? final_cheque_status { get; set; }
    public string? ho_cheque_status { get; set; }
    public string? courier_type { get; set; }
    public string? consignment_no { get; set; }
    public string? section_138_eligible_yn { get; set; }

    public SecurityCheque? ToDomain(DealerUrn urn)
    {
        if (string.IsNullOrWhiteSpace(cheque_no))
            return null;

        if (!OdosChequeStatusMapper.TryMap(cheque_status, out var status))
            return null;

        return new SecurityCheque(
            urn,
            cheque_no.Trim(),
            new Money(amount, "INR"),
            status,
            micr: null,
            depositDate: null,
            validityEnd: null,
            extractionConfidence: null);
    }
}

internal static class OdosChequeStatusMapper
{
    /// <summary>Maps production cheque_status values REALIZED and BOUNCED only.</summary>
    public static bool TryMap(string? odosStatus, out ChequeStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(odosStatus))
            return false;

        switch (odosStatus.Trim().ToUpperInvariant())
        {
            case "REALIZED":
                status = ChequeStatus.Realised;
                return true;
            case "BOUNCED":
                status = ChequeStatus.Bounced;
                return true;
            default:
                return false;
        }
    }
}
