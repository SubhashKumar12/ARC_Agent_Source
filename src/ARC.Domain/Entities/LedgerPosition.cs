using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

/// <summary>AR document line. Ageing is pinned to due date, not document date (odos_90).</summary>
public sealed class LedgerPosition
{
    public DealerUrn DealerUrn { get; }
    public string DocumentType { get; }
    public DateOnly DueDate { get; }
    public DateOnly PostedOn { get; }
    public Money Amount { get; }
    public LineItemRef Lineage { get; }

    public LedgerPosition(
        DealerUrn dealerUrn,
        string documentType,
        DateOnly dueDate,
        DateOnly postedOn,
        Money amount,
        LineItemRef lineage)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Document type is required.", nameof(documentType));

        DealerUrn = dealerUrn;
        DocumentType = documentType.Trim();
        DueDate = dueDate;
        PostedOn = postedOn;
        Amount = amount;
        Lineage = lineage;
    }
}
