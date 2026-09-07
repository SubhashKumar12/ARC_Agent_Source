namespace ARC.Knowledge.Vector;

/// <summary>
/// Cosmos documents item. Partition key is <c>documentType</c>. Not a financial authority record.
/// </summary>
public sealed class IndexedDocument
{
    public string id { get; set; } = "";
    public string documentType { get; set; } = "";
    public string? documentCategory { get; set; }
    public string status { get; set; } = "";
    public string? version { get; set; }
    public string[]? regionScope { get; set; }
    public string? dealerUrn { get; set; }
    public string? sourceDocumentId { get; set; }
    public string? blobLocation { get; set; }
    public string? pageOrSection { get; set; }
    public string title { get; set; } = "";
    public string content { get; set; } = "";
    public string? contentHash { get; set; }
    public float[]? embedding { get; set; }
    public string? embeddingModel { get; set; }
    public DateTimeOffset? embeddedUtc { get; set; }
    public double? similarityScore { get; set; }
}
