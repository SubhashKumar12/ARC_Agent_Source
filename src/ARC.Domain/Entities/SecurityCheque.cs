using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class SecurityCheque
{
    public DealerUrn DealerUrn { get; }
    public string ChequeNumber { get; }
    public string? Micr { get; }
    public Money Amount { get; }
    public ChequeStatus Status { get; }
    public DateOnly? DepositDate { get; }
    public DateOnly? ValidityEnd { get; }
    public decimal? ExtractionConfidence { get; }

    public SecurityCheque(
        DealerUrn dealerUrn,
        string chequeNumber,
        Money amount,
        ChequeStatus status,
        string? micr = null,
        DateOnly? depositDate = null,
        DateOnly? validityEnd = null,
        decimal? extractionConfidence = null)
    {
        if (string.IsNullOrWhiteSpace(chequeNumber))
            throw new ArgumentException("Cheque number is required.", nameof(chequeNumber));

        DealerUrn = dealerUrn;
        ChequeNumber = chequeNumber.Trim();
        Amount = amount;
        Status = status;
        Micr = micr;
        DepositDate = depositDate;
        ValidityEnd = validityEnd;
        ExtractionConfidence = extractionConfidence;
    }

    public bool PresentationWithinValidity(DateOnly presentationDate)
        => ValidityEnd is null || presentationDate <= ValidityEnd.Value;
}
