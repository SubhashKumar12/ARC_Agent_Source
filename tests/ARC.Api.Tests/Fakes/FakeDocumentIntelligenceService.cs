using ARC.Knowledge.Documents;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for Document Intelligence. Does not call real Azure service.
/// </summary>
public sealed class FakeDocumentIntelligenceService : IDocumentIntelligenceService
{
    public Task<DocumentExtractionResult> ExtractAsync(Stream content, DocumentMetadata metadata, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DocumentExtractionResult
        {
            Metadata = metadata,
            Status = ExtractionStatus.Failed
        });
    }

    public ExtractionDiscrepancy CompareCheque(ChequeExtraction extracted, KeyedChequeFields keyed)
        => ChequeComparison.Compare(extracted, keyed);
}
