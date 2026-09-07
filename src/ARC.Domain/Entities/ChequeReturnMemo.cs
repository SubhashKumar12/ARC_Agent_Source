using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class ChequeReturnMemo
{
    public DealerUrn DealerUrn { get; }
    public string ChequeNumber { get; }
    public string ReturnReasonCode { get; }
    public DateOnly MemoIssueDate { get; }
    public DateOnly MemoReceivedDate { get; }
    public decimal? ExtractionConfidence { get; }

    public ChequeReturnMemo(
        DealerUrn dealerUrn,
        string chequeNumber,
        string returnReasonCode,
        DateOnly memoIssueDate,
        DateOnly memoReceivedDate,
        decimal? extractionConfidence = null)
    {
        if (string.IsNullOrWhiteSpace(chequeNumber))
            throw new ArgumentException("Cheque number is required.", nameof(chequeNumber));
        if (string.IsNullOrWhiteSpace(returnReasonCode))
            throw new ArgumentException("Return reason code is required.", nameof(returnReasonCode));

        DealerUrn = dealerUrn;
        ChequeNumber = chequeNumber.Trim();
        ReturnReasonCode = returnReasonCode.Trim().ToUpperInvariant();
        MemoIssueDate = memoIssueDate;
        MemoReceivedDate = memoReceivedDate;
        ExtractionConfidence = extractionConfidence;
    }
}
