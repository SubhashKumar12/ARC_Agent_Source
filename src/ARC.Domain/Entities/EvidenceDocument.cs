using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class EvidenceDocument
{
    public DealerUrn DealerUrn { get; }
    public DocumentType Type { get; }
    public string Location { get; }

    public EvidenceDocument(DealerUrn dealerUrn, DocumentType type, string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            throw new ArgumentException("Document location is required.", nameof(location));
        DealerUrn = dealerUrn;
        Type = type;
        Location = location.Trim();
    }
}
