using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class Dispute
{
    public DealerUrn DealerUrn { get; }
    public DisputeStatus Status { get; }
    public string Reference { get; }

    public Dispute(DealerUrn dealerUrn, DisputeStatus status, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Dispute reference is required.", nameof(reference));
        DealerUrn = dealerUrn;
        Status = status;
        Reference = reference.Trim();
    }

    public bool BlocksNotice => Status == DisputeStatus.UnderReview;
}
