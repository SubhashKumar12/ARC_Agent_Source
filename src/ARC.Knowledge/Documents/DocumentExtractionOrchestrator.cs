using ARC.Data.Blob;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Knowledge.Documents;

public interface IDocumentExtractionOrchestrator
{
    Task<DocumentValidationResult> ValidateForSection138Async(
        DealerUrn dealerUrn,
        IReadOnlyList<ExtractionDocumentRef> evidence,
        string? correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Blob → hash → cache → extract → compare vs keyed SQL. Does not write SQL legal/financial facts.
/// </summary>
public sealed class DocumentExtractionOrchestrator : IDocumentExtractionOrchestrator
{
    private static readonly DocumentType[] Section138DiTypes =
    [
        DocumentType.SecurityChequeImage,
        DocumentType.ChequeReturnMemo,
        DocumentType.CourierPod
    ];

    private readonly IDocumentIntelligenceService _intelligence;
    private readonly IEvidenceDocumentRepository _evidence;
    private readonly IChequeRepository _cheques;
    private readonly IDocumentExtractionStore _store;
    private readonly ArcKnowledgeOptions _options;
    private readonly ILogger<DocumentExtractionOrchestrator> _logger;

    public DocumentExtractionOrchestrator(
        IDocumentIntelligenceService intelligence,
        IEvidenceDocumentRepository evidence,
        IChequeRepository cheques,
        IDocumentExtractionStore store,
        IOptions<ArcKnowledgeOptions> options,
        ILogger<DocumentExtractionOrchestrator> logger)
    {
        _intelligence = intelligence;
        _evidence = evidence;
        _cheques = cheques;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentValidationResult> ValidateForSection138Async(
        DealerUrn dealerUrn,
        IReadOnlyList<ExtractionDocumentRef> evidence,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var items = (evidence ?? [])
            .Where(e => Section138DiTypes.Contains(e.Type) && !string.IsNullOrWhiteSpace(e.Location))
            .GroupBy(e => e.Type)
            .Select(g => g.First())
            .ToList();

        if (items.Count == 0)
        {
            _logger.LogInformation(
                "Document validation skipped dealer {DealerUrn} correlation {CorrelationId} (no DI artefacts)",
                dealerUrn.Value,
                correlationId);
            return new DocumentValidationResult
            {
                CanProgress = true,
                Reason = "No Document Intelligence artefacts on the run.",
                Extractions = []
            };
        }

        var cheques = await _cheques.ListChequesAsync(dealerUrn, cancellationToken);
        var memos = await _cheques.ListReturnMemosAsync(dealerUrn, cancellationToken);
        var keyedCheque = ChequeSelection.Select(cheques, memos);
        var keyedMemo = keyedCheque is null
            ? null
            : memos.FirstOrDefault(m => string.Equals(m.ChequeNumber, keyedCheque.ChequeNumber, StringComparison.OrdinalIgnoreCase));

        var persisted = new List<PersistedDocumentExtraction>();
        var usedCache = false;

        foreach (var item in items)
        {
            var result = await ProcessAsync(dealerUrn, item, keyedCheque, keyedMemo, correlationId, cancellationToken);
            if (result.Extraction is null)
            {
                return new DocumentValidationResult
                {
                    CanProgress = false,
                    Reason = result.BlockReason ?? "Document extraction requires Depot Admin review.",
                    Extractions = persisted,
                    UsedCache = usedCache
                };
            }

            usedCache |= result.FromCache;
            persisted.Add(result.Extraction);

            if (!result.CanProgress)
            {
                return new DocumentValidationResult
                {
                    CanProgress = false,
                    Reason = result.BlockReason ?? "Document extraction requires Depot Admin review.",
                    Extractions = persisted,
                    UsedCache = usedCache
                };
            }
        }

        _logger.LogInformation(
            "Document validation passed dealer {DealerUrn} correlation {CorrelationId} artefacts {Count} cache {UsedCache}",
            dealerUrn.Value,
            correlationId,
            persisted.Count,
            usedCache);

        return new DocumentValidationResult
        {
            CanProgress = true,
            Reason = "Document extraction validated against keyed facts.",
            Extractions = persisted,
            UsedCache = usedCache
        };
    }

    private async Task<StepResult> ProcessAsync(
        DealerUrn dealerUrn,
        ExtractionDocumentRef item,
        SecurityCheque? keyedCheque,
        ChequeReturnMemo? keyedMemo,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var document = new EvidenceDocument(dealerUrn, item.Type, item.Location);
        byte[] bytes;
        try
        {
            await using var stream = await _evidence.DownloadAsync(document, cancellationToken);
            bytes = await ReadAllBytesAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Evidence download failed document {DocumentId} type {DocumentType} correlation {CorrelationId}",
                $"{dealerUrn.Value}/{item.Type}",
                item.Type,
                correlationId);

            if (_intelligence is DemoDocumentIntelligenceService)
            {
                bytes = System.Text.Encoding.UTF8.GetBytes(item.Location);
            }
            else if (item.Type == DocumentType.CourierPod)
            {
                return StepResult.Ok(null, fromCache: false);
            }
            else
            {
                return StepResult.Block("Document extraction requires Depot Admin review (document unavailable).");
            }
        }

        if (bytes.Length == 0 && item.Type != DocumentType.CourierPod && _intelligence is not DemoDocumentIntelligenceService)
            return StepResult.Block("Document extraction requires Depot Admin review (empty document).");

        var contentHash = DocumentContentHasher.ComputeSha256(bytes);
        var modelId = ModelIdFor(item.Type);

        PersistedDocumentExtraction? cached = null;
        try
        {
            cached = await _store.GetAsync(contentHash, modelId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Extraction cache read failed document {DocumentId} correlation {CorrelationId}",
                $"{dealerUrn.Value}/{item.Type}",
                correlationId);
        }

        PersistedDocumentExtraction persisted;
        var fromCache = false;
        if (cached is not null)
        {
            persisted = cached;
            fromCache = true;
            _logger.LogInformation(
                "Extraction cache hit document {DocumentId} type {DocumentType} correlation {CorrelationId}",
                cached.DocumentId,
                item.Type,
                correlationId);
        }
        else
        {
            DocumentExtractionResult extracted;
            try
            {
                await using var extractStream = new MemoryStream(bytes, writable: false);
                var metadata = new DocumentMetadata(
                    DocumentId: $"{dealerUrn.Value}/{item.Type}",
                    DocumentType: item.Type,
                    BlobLocation: item.Location,
                    Version: modelId,
                    Status: "ACTIVE",
                    RegionScope: null,
                    DealerUrn: dealerUrn,
                    CorrelationId: correlationId,
                    CapturedUtc: DateTimeOffset.UtcNow);
                extracted = await _intelligence.ExtractAsync(extractStream, metadata, cancellationToken);
            }
            catch (UnsupportedDocumentException)
            {
                if (item.Type == DocumentType.CourierPod)
                    return StepResult.Ok(null, fromCache: false);
                return StepResult.Block("Document extraction requires Depot Admin review (unsupported document).");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Extraction timeout or cancel document {DocumentId} correlation {CorrelationId}",
                    $"{dealerUrn.Value}/{item.Type}",
                    correlationId);
                return StepResult.Block("Document extraction requires Depot Admin review (timeout).");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Extraction failed document {DocumentId} correlation {CorrelationId}",
                    $"{dealerUrn.Value}/{item.Type}",
                    correlationId);
                if (item.Type == DocumentType.CourierPod)
                    return StepResult.Ok(null, fromCache: false);
                return StepResult.Block("Document extraction requires Depot Admin review (extraction failed).");
            }

            persisted = ToPersisted(extracted, contentHash, modelId, dealerUrn, item, keyedCheque, keyedMemo);
            try
            {
                await _store.SaveAsync(persisted, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Extraction cache write failed document {DocumentId} correlation {CorrelationId}",
                    persisted.DocumentId,
                    correlationId);
                return StepResult.Block("Document extraction requires Depot Admin review (cache unavailable).");
            }
        }

        if (item.Type == DocumentType.CourierPod)
            return StepResult.Ok(persisted, fromCache);

        if (persisted.Status is ExtractionStatus.Failed or ExtractionStatus.Unsupported)
            return StepResult.Block("Document extraction requires Depot Admin review.", persisted, fromCache);

        if (item.Type == DocumentType.SecurityChequeImage)
        {
            if (keyedCheque is null)
                return StepResult.Block("Document extraction requires Depot Admin review (no keyed cheque).", persisted, fromCache);

            if (persisted.Status == ExtractionStatus.LowConfidence
                || persisted.Cheque is null
                || !persisted.Cheque.MeetsAutoAcceptThreshold(
                    _options.ChequeNumberConfidence,
                    _options.MicrConfidence,
                    _options.AmountConfidence))
            {
                return StepResult.Block("Document extraction requires Depot Admin review (low confidence or missing required field).", persisted, fromCache);
            }

            var discrepancy = persisted.Discrepancy ?? _intelligence.CompareCheque(
                persisted.Cheque,
                new KeyedChequeFields(keyedCheque.ChequeNumber, keyedCheque.Micr, keyedCheque.Amount.Amount));
            persisted = persisted with { Discrepancy = discrepancy };
            if (discrepancy.HasMismatch)
                return StepResult.Block("Document extraction requires Depot Admin review (keyed mismatch).", persisted, fromCache);
        }

        if (item.Type == DocumentType.ChequeReturnMemo)
        {
            if (persisted.MemoReasonDiscrepancy || persisted.MemoDateDiscrepancy)
                return StepResult.Block("Document extraction requires Depot Admin review (return memo discrepancy).", persisted, fromCache);
        }

        return StepResult.Ok(persisted, fromCache);
    }

    private PersistedDocumentExtraction ToPersisted(
        DocumentExtractionResult extracted,
        string contentHash,
        string modelId,
        DealerUrn dealerUrn,
        ExtractionDocumentRef item,
        SecurityCheque? keyedCheque,
        ChequeReturnMemo? keyedMemo)
    {
        ExtractionDiscrepancy? discrepancy = null;
        var memoReason = false;
        var memoDate = false;

        if (item.Type == DocumentType.SecurityChequeImage && extracted.Cheque is not null && keyedCheque is not null)
        {
            discrepancy = _intelligence.CompareCheque(
                extracted.Cheque,
                new KeyedChequeFields(keyedCheque.ChequeNumber, keyedCheque.Micr, keyedCheque.Amount.Amount));
        }

        if (item.Type == DocumentType.ChequeReturnMemo && extracted.ReturnMemo is not null && keyedMemo is not null)
        {
            if (!string.IsNullOrWhiteSpace(extracted.ReturnMemo.ReturnReasonCode)
                && !string.Equals(extracted.ReturnMemo.ReturnReasonCode, keyedMemo.ReturnReasonCode, StringComparison.OrdinalIgnoreCase))
            {
                memoReason = true;
            }

            if (extracted.ReturnMemo.MemoDate is { } extractedDate
                && extractedDate != keyedMemo.MemoReceivedDate
                && extractedDate != keyedMemo.MemoIssueDate)
            {
                memoDate = true;
            }
        }

        var status = extracted.Status;
        if (item.Type == DocumentType.SecurityChequeImage
            && extracted.Cheque is not null
            && !extracted.Cheque.MeetsAutoAcceptThreshold(
                _options.ChequeNumberConfidence,
                _options.MicrConfidence,
                _options.AmountConfidence))
        {
            status = ExtractionStatus.LowConfidence;
        }

        var memo = extracted.ReturnMemo is null
            ? null
            : extracted.ReturnMemo with { LayoutText = null };

        return new PersistedDocumentExtraction
        {
            DocumentId = extracted.Metadata.DocumentId,
            DealerUrn = dealerUrn.Value,
            DocumentType = item.Type,
            BlobLocation = item.Location,
            Status = status,
            Cheque = extracted.Cheque,
            ReturnMemo = memo,
            Discrepancy = discrepancy,
            MemoReasonDiscrepancy = memoReason,
            MemoDateDiscrepancy = memoDate,
            ContentHash = contentHash,
            ModelId = modelId,
            ExtractedUtc = extracted.ExtractedUtc,
            CorrelationId = extracted.Metadata.CorrelationId,
            Fields = extracted.Fields
        };
    }

    private string ModelIdFor(DocumentType type)
        => type == DocumentType.SecurityChequeImage ? _options.ChequeModelId : _options.LayoutModelId;

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private sealed record StepResult(
        bool CanProgress,
        string? BlockReason,
        PersistedDocumentExtraction? Extraction,
        bool FromCache)
    {
        public static StepResult Ok(PersistedDocumentExtraction? extraction, bool fromCache)
            => new(true, null, extraction, fromCache);

        public static StepResult Block(string reason, PersistedDocumentExtraction? extraction = null, bool fromCache = false)
            => new(false, reason, extraction, fromCache);
    }
}
