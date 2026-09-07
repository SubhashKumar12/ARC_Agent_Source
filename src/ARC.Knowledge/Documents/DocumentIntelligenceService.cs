using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Domain.Enums;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Exceptions;

namespace ARC.Knowledge.Documents;

public sealed class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly ArcKnowledgeOptions _options;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly object _gate = new();
    private DocumentIntelligenceClient? _client;

    public DocumentIntelligenceService(
        IOptions<ArcKnowledgeOptions> options,
        ILogger<DocumentIntelligenceService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        Stream content,
        DocumentMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!UsesDocumentIntelligence(metadata.DocumentType))
            throw new UnsupportedDocumentException(metadata.DocumentType.ToString());

        var modelId = metadata.DocumentType == DocumentType.SecurityChequeImage
            ? _options.ChequeModelId
            : _options.LayoutModelId;

        _logger.LogInformation(
            "Extracting document {DocumentId} type {DocumentType} correlation {CorrelationId}",
            metadata.DocumentId,
            metadata.DocumentType,
            metadata.CorrelationId);

        try
        {
            var binary = await BinaryData.FromStreamAsync(content, cancellationToken);
            if (binary.ToMemory().Length == 0)
                throw new ExtractionFailedException(metadata.DocumentId);

            return await AnalyzeWithModelAsync(binary, modelId, metadata, allowFallback: true, cancellationToken);
        }
        catch (UnsupportedDocumentException)
        {
            throw;
        }
        catch (ExtractionFailedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Extraction canceled document {DocumentId}", metadata.DocumentId);
            throw new ExtractionFailedException(metadata.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for document {DocumentId}", metadata.DocumentId);
            throw new ExtractionFailedException(metadata.DocumentId, ex);
        }
    }

    public ExtractionDiscrepancy CompareCheque(ChequeExtraction extracted, KeyedChequeFields keyed)
        => ChequeComparison.Compare(extracted, keyed);

    private async Task<DocumentExtractionResult> AnalyzeWithModelAsync(
        BinaryData binary,
        string modelId,
        DocumentMetadata metadata,
        bool allowFallback,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seconds = _options.DocumentIntelligenceTimeoutSeconds <= 0 ? 120 : _options.DocumentIntelligenceTimeoutSeconds;
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

        try
        {
            var operation = await Client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                modelId,
                binary,
                cancellationToken: timeout.Token);
            return Map(operation.Value, metadata);
        }
        catch (Exception ex) when (allowFallback
            && metadata.DocumentType == DocumentType.SecurityChequeImage
            && !string.IsNullOrWhiteSpace(_options.ChequeModelFallbackId)
            && !string.Equals(modelId, _options.ChequeModelFallbackId, StringComparison.OrdinalIgnoreCase)
            && ex is not OperationCanceledException and not UnsupportedDocumentException)
        {
            _logger.LogWarning(
                "Primary cheque model failed document {DocumentId}; retrying fallback model",
                metadata.DocumentId);
            return await AnalyzeWithModelAsync(
                binary,
                _options.ChequeModelFallbackId,
                metadata,
                allowFallback: false,
                cancellationToken);
        }
    }

    private DocumentIntelligenceClient Client
    {
        get
        {
            if (_client is not null)
                return _client;

            lock (_gate)
            {
                if (_client is not null)
                    return _client;
                if (string.IsNullOrWhiteSpace(_options.DocumentIntelligenceEndpoint))
                    throw new KnowledgeException("ArcKnowledge:DocumentIntelligenceEndpoint is not configured.");
                if (!_options.UseManagedIdentity)
                    throw new KnowledgeException("Document Intelligence requires Managed Identity. API keys are not supported.");

                _client = new DocumentIntelligenceClient(
                    new Uri(_options.DocumentIntelligenceEndpoint),
                    new DefaultAzureCredential());
                return _client;
            }
        }
    }

    private DocumentExtractionResult Map(AnalyzeResult analyze, DocumentMetadata metadata)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = analyze.Documents?.FirstOrDefault();
        if (doc?.Fields is not null)
        {
            foreach (var pair in doc.Fields)
            {
                if (pair.Value.Content is { Length: > 0 } text)
                    fields[pair.Key] = text;
            }
        }

        var layout = analyze.Content;
        ChequeExtraction? cheque = null;
        ReturnMemoExtraction? memo = null;

        if (metadata.DocumentType == DocumentType.SecurityChequeImage)
        {
            cheque = new ChequeExtraction
            {
                BankName = Field(fields, "BankName", "PayingBank"),
                BankNameConfidence = Conf(doc, "BankName"),
                ChequeNumber = Field(fields, "ChequeNumber", "CheckNumber", "Number"),
                ChequeNumberConfidence = Conf(doc, "ChequeNumber") ?? Conf(doc, "CheckNumber"),
                MicrLine = Field(fields, "MICR", "MicrLine"),
                MicrConfidence = Conf(doc, "MICR") ?? Conf(doc, "MicrLine"),
                Amount = ParseAmount(Field(fields, "Amount", "AmountNumeric")),
                AmountConfidence = Conf(doc, "Amount"),
                AccountName = Field(fields, "AccountName", "Payer"),
                AccountNameConfidence = Conf(doc, "AccountName") ?? Conf(doc, "Payer"),
                ChequeDate = ParseDate(Field(fields, "Date", "ChequeDate", "IssueDate", "CheckDate")),
                DateConfidence = Conf(doc, "Date") ?? Conf(doc, "ChequeDate") ?? Conf(doc, "IssueDate") ?? Conf(doc, "CheckDate")
            };
        }
        else if (metadata.DocumentType == DocumentType.ChequeReturnMemo)
        {
            memo = new ReturnMemoExtraction(
                Field(fields, "Reason", "ReturnReason"),
                Field(fields, "ReasonCode", "ReturnReasonCode")?.ToUpperInvariant(),
                Conf(doc, "ReasonCode") ?? Conf(doc, "ReturnReason"),
                ParseDate(Field(fields, "Date", "MemoDate")),
                layout);
        }

        var status = ExtractionStatus.Succeeded;
        if (cheque is not null
            && !cheque.MeetsAutoAcceptThreshold(
                _options.ChequeNumberConfidence,
                _options.MicrConfidence,
                _options.AmountConfidence))
        {
            status = ExtractionStatus.LowConfidence;
        }

        return new DocumentExtractionResult
        {
            Metadata = metadata,
            Status = status,
            Cheque = cheque,
            ReturnMemo = memo,
            LayoutText = layout,
            Fields = fields,
            ExtractedUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool UsesDocumentIntelligence(DocumentType type) => type is
        DocumentType.SecurityChequeImage or
        DocumentType.ChequeReturnMemo or
        DocumentType.CourierPod;

    private static string? Field(IReadOnlyDictionary<string, string> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static decimal? Conf(AnalyzedDocument? doc, string name)
    {
        if (doc?.Fields is null || !doc.Fields.TryGetValue(name, out var field))
            return null;
        return (decimal?)field.Confidence;
    }

    private static decimal? ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(digits, out var value) ? value : null;
    }

    private static DateOnly? ParseDate(string? raw)
        => DateOnly.TryParse(raw, out var date) ? date : null;
}
