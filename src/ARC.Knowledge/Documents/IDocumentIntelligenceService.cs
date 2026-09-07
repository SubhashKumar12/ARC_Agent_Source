using ARC.Domain.Enums;

namespace ARC.Knowledge.Documents;

public interface IDocumentIntelligenceService
{
    Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        DocumentMetadata metadata,
        CancellationToken cancellationToken);

    ExtractionDiscrepancy CompareCheque(ChequeExtraction extracted, KeyedChequeFields keyed);
}
