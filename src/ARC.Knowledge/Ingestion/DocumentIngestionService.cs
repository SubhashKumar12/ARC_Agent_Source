using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Orchestrates document ingestion with chunking, deduplication, embedding, and persistence.
/// Implements embed-once, hash-based dedupe, and cost controls.
/// Ingests <see cref="SourceDocument"/> knowledge text only.
/// Do not embed SQL ledger, cheque, outstanding, eligibility, or other transactional rows.
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IEnumerable<IDocumentChunker> _chunkers;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IIndexedDocumentStore _documentStore;
    private readonly IContentSanitizer _sanitizer;
    private readonly ArcKnowledgeOptions _options;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        IEnumerable<IDocumentChunker> chunkers,
        IEmbeddingProvider embeddingProvider,
        IIndexedDocumentStore documentStore,
        IContentSanitizer sanitizer,
        IOptions<ArcKnowledgeOptions> options,
        ILogger<DocumentIngestionService> logger)
    {
        _chunkers = chunkers;
        _embeddingProvider = embeddingProvider;
        _documentStore = documentStore;
        _sanitizer = sanitizer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestDocumentAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        return await IngestDocumentsAsync(new[] { document }, cancellationToken);
    }

    public async Task<IngestionResult> IngestDocumentsAsync(
        IEnumerable<SourceDocument> documents,
        CancellationToken cancellationToken = default)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));

        var builder = new IngestionResultBuilder();
        var documentList = documents.ToList();

        _logger.LogInformation("Starting ingestion of {Count} documents", documentList.Count);

        foreach (var document in documentList)
        {
            try
            {
                await IngestSingleDocumentAsync(document, builder, cancellationToken);
                builder.IncrementDocumentsProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest document {DocumentId}", document.SourceDocumentId);
                builder.AddError($"Document {document.SourceDocumentId}: {ex.Message}");
            }
        }

        var result = builder.Build();
        _logger.LogInformation("{Result}", result);

        return result;
    }

    private async Task IngestSingleDocumentAsync(
        SourceDocument document,
        IngestionResultBuilder builder,
        CancellationToken cancellationToken)
    {
        // 1. Select appropriate chunker
        var chunker = SelectChunker(document.DocumentCategory);
        if (chunker == null)
        {
            _logger.LogWarning("No chunker found for category {Category}, skipping document {DocumentId}",
                document.DocumentCategory, document.SourceDocumentId);
            builder.AddError($"No chunker for category {document.DocumentCategory}");
            return;
        }

        // 2. Chunk document
        var chunks = chunker.ChunkDocument(document);
        builder.IncrementChunksProduced(chunks.Count);

        _logger.LogDebug("Document {DocumentId} chunked into {ChunkCount} chunks",
            document.SourceDocumentId, chunks.Count);

        var incomingChunkIds = new HashSet<string>(
            chunks.Select(c => IndexedDocumentId.Create(
                document.SourceDocumentId,
                document.Version,
                c.PageOrSection)),
            StringComparer.Ordinal);

        // 3. Upsert current chunks (reactivation / embed-once happen here)
        foreach (var chunk in chunks)
        {
            try
            {
                await ProcessChunkAsync(document, chunk, builder, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process chunk {ChunkId} from document {DocumentId}",
                    chunk.PageOrSection, document.SourceDocumentId);
                builder.IncrementChunksFailed();
                builder.AddError($"Chunk {chunk.PageOrSection}: {ex.Message}");
            }
        }

        // 4. Retire same-source/same-version ACTIVE chunks absent from this ingestion.
        // No embedding, no LLM, no hard delete — status flip only.
        await RetireRemovedChunksAsync(document, incomingChunkIds, cancellationToken);
    }

    private async Task RetireRemovedChunksAsync(
        SourceDocument document,
        HashSet<string> incomingChunkIds,
        CancellationToken cancellationToken)
    {
        var existing = await _documentStore.QueryBySourceAndVersionAsync(
            document.DocumentType,
            document.SourceDocumentId,
            document.Version,
            cancellationToken);

        foreach (var stored in existing)
        {
            if (!string.Equals(stored.status, ArcKnowledgeOptions.ActiveStatus, StringComparison.OrdinalIgnoreCase))
                continue;

            if (incomingChunkIds.Contains(stored.id))
                continue;

            // Preserve embedding, hash, provenance; mark non-retrievable for normal RAG.
            stored.status = ArcKnowledgeOptions.InactiveStatus;
            await _documentStore.UpsertAsync(stored, cancellationToken);

            _logger.LogInformation(
                "Retired chunk {ChunkId} for source {SourceDocumentId} version {Version}",
                stored.id,
                document.SourceDocumentId,
                document.Version);
        }
    }

    private async Task ProcessChunkAsync(
        SourceDocument document,
        ChunkResult chunk,
        IngestionResultBuilder builder,
        CancellationToken cancellationToken)
    {
        // 1. Sanitize content before hashing/embedding
        var sanitizedContent = _sanitizer.Sanitize(chunk.Content);

        // 2. Compute content hash
        var contentHash = ContentHasher.ComputeHash(sanitizedContent);

        // 3. Get embedding model name from provider
        var embeddingModel = GetEmbeddingModelName();

        // 4. Check if chunk already exists with same content + embedding model
        var existing = await _documentStore.FindByContentHashAsync(
            contentHash,
            embeddingModel,
            cancellationToken);

        float[]? embedding = null;

        if (existing != null)
        {
            // Content unchanged, reuse existing embedding
            _logger.LogDebug("Chunk {ChunkId} unchanged, reusing embedding", chunk.PageOrSection);
            embedding = existing.embedding;
            builder.IncrementChunksSkipped();
        }
        else
        {
            // Content changed or new, generate embedding
            embedding = await GenerateEmbeddingAsync(sanitizedContent, cancellationToken);
            
            if (embedding != null)
            {
                ValidateEmbeddingDimensions(embedding);
                builder.IncrementChunksEmbedded();
            }
            else
            {
                _logger.LogWarning("Embedding generation returned null for chunk {ChunkId}", chunk.PageOrSection);
            }
        }

        // 5. Create indexed document
        var indexedDocument = new IndexedDocument
        {
            id = IndexedDocumentId.Create(
                document.SourceDocumentId,
                document.Version,
                chunk.PageOrSection),
            documentType = document.DocumentType,
            documentCategory = document.DocumentCategory,
            status = document.Status,
            version = document.Version,
            regionScope = document.RegionScope,
            dealerUrn = document.DealerUrn,
            sourceDocumentId = document.SourceDocumentId,
            blobLocation = document.BlobLocation,
            pageOrSection = chunk.PageOrSection,
            title = chunk.Title,
            content = sanitizedContent,
            contentHash = contentHash,
            embedding = embedding,
            embeddingModel = embeddingModel,
            embeddedUtc = embedding != null ? DateTimeOffset.UtcNow : existing?.embeddedUtc
        };

        // 6. Upsert to store
        await _documentStore.UpsertAsync(indexedDocument, cancellationToken);

        _logger.LogDebug("Chunk {ChunkId} persisted successfully", chunk.PageOrSection);
    }

    private IDocumentChunker? SelectChunker(string documentCategory)
    {
        return _chunkers.FirstOrDefault(c => c.SupportedCategories.Contains(documentCategory));
    }

    private async Task<float[]?> GenerateEmbeddingAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(
                new[] { content },
                cancellationToken);

            return embeddings.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding generation failed");
            throw;
        }
    }

    private void ValidateEmbeddingDimensions(float[] embedding)
    {
        var expectedDimensions = _options.Embeddings.Dimensions;

        if (embedding.Length != expectedDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: expected {expectedDimensions}, got {embedding.Length}. " +
                $"Verify ArcKnowledge:Embeddings:Dimensions configuration matches deployment.");
        }
    }

    private string GetEmbeddingModelName()
    {
        // Use configured deployment name as model identifier
        var deployment = _options.Embeddings.Deployment;
        
        if (string.IsNullOrWhiteSpace(deployment))
        {
            return "none";
        }

        return $"{deployment}:{_options.Embeddings.Dimensions}d";
    }
}
