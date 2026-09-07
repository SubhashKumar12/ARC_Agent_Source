namespace ARC.Domain.ValueObjects;

/// <summary>Retrievable source for a notice justification claim (citation faithfulness).</summary>
public sealed record Citation
{
    public string SourceId { get; }
    public string Description { get; }

    public Citation(string sourceId, string description)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Citation SourceId is required.", nameof(sourceId));
        SourceId = sourceId;
        Description = description;
    }
}
