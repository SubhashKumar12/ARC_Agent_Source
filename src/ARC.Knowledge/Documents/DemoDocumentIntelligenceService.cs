using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using ARC.Knowledge.Configuration;

namespace ARC.Knowledge.Documents;

/// <summary>
/// Local/demo Document Intelligence. Never calls Azure. Synthesizes extraction from
/// seeded SQL cheque/memo facts so Shadow S3 can validate without scans.
/// Does not decide eligibility or clocks.
/// </summary>
public sealed class DemoDocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly IChequeRepository _cheques;
    private readonly ArcKnowledgeOptions _options;

    public DemoDocumentIntelligenceService(IChequeRepository cheques, IOptions<ArcKnowledgeOptions> options)
    {
        _cheques = cheques;
        _options = options.Value;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        DocumentMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.DocumentType is not (
            DocumentType.SecurityChequeImage or
            DocumentType.ChequeReturnMemo or
            DocumentType.CourierPod))
        {
            throw new Exceptions.UnsupportedDocumentException(metadata.DocumentType.ToString());
        }

        if (metadata.DealerUrn is not { } dealerUrn)
        {
            return Failed(metadata, "DealerUrn is required for demo extraction.");
        }

        var cheques = await _cheques.ListChequesAsync(dealerUrn, cancellationToken);
        var memos = await _cheques.ListReturnMemosAsync(dealerUrn, cancellationToken);
        var cheque = ChequeSelection.Select(cheques, memos);
        var memo = cheque is null
            ? null
            : memos.FirstOrDefault(m => string.Equals(m.ChequeNumber, cheque.ChequeNumber, StringComparison.OrdinalIgnoreCase));

        ChequeExtraction? chequeExtraction = null;
        ReturnMemoExtraction? memoExtraction = null;

        if (metadata.DocumentType == DocumentType.SecurityChequeImage)
        {
            if (cheque is null)
                return Failed(metadata, "No keyed cheque is available for demo extraction.");

            chequeExtraction = new ChequeExtraction
            {
                ChequeNumber = cheque.ChequeNumber,
                ChequeNumberConfidence = Math.Max(_options.ChequeNumberConfidence, 0.99m),
                MicrLine = cheque.Micr,
                MicrConfidence = string.IsNullOrWhiteSpace(cheque.Micr)
                    ? null
                    : Math.Max(_options.MicrConfidence, 0.99m),
                Amount = cheque.Amount.Amount,
                AmountConfidence = Math.Max(_options.AmountConfidence, 0.99m),
                ChequeDate = cheque.DepositDate,
                DateConfidence = cheque.DepositDate is null ? null : 0.99m
            };
        }
        else if (metadata.DocumentType == DocumentType.ChequeReturnMemo)
        {
            if (memo is null)
                return Failed(metadata, "No keyed return memo is available for demo extraction.");

            memoExtraction = new ReturnMemoExtraction(
                memo.ReturnReasonCode,
                memo.ReturnReasonCode,
                0.99m,
                memo.MemoReceivedDate,
                LayoutText: null);
        }

        return new DocumentExtractionResult
        {
            Metadata = metadata,
            Status = ExtractionStatus.Succeeded,
            Cheque = chequeExtraction,
            ReturnMemo = memoExtraction,
            ExtractedUtc = DateTimeOffset.UtcNow
        };
    }

    public ExtractionDiscrepancy CompareCheque(ChequeExtraction extracted, KeyedChequeFields keyed)
        => ChequeComparison.Compare(extracted, keyed);

    private static DocumentExtractionResult Failed(DocumentMetadata metadata, string error)
        => new()
        {
            Metadata = metadata,
            Status = ExtractionStatus.Failed,
            Error = error,
            ExtractedUtc = DateTimeOffset.UtcNow
        };
}
