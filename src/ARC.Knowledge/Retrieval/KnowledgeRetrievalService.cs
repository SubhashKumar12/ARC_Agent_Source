using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Graph;

namespace ARC.Knowledge.Retrieval;

public interface IKnowledgeRetrievalService
{
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Graph traversal plus metadata-filtered document retrieval.
/// Does not decide eligibility, notices, amounts, or legal progression.
/// </summary>
public sealed class KnowledgeRetrievalService : IKnowledgeRetrievalService
{
    private readonly IGraphTraversal _graph;
    private readonly IDocumentRetriever _retriever;
    private readonly IEmbeddingProvider _embeddings;
    private readonly ArcKnowledgeOptions _options;
    private readonly ILogger<KnowledgeRetrievalService> _logger;

    public KnowledgeRetrievalService(
        IGraphTraversal graph,
        IDocumentRetriever retriever,
        IEmbeddingProvider embeddings,
        IOptions<ArcKnowledgeOptions> options,
        ILogger<KnowledgeRetrievalService> logger)
    {
        _graph = graph;
        _retriever = retriever;
        _embeddings = embeddings;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken cancellationToken)
    {
        var topK = _options.ClampTopK(query.TopK);
        IReadOnlyList<GraphNode> nodes = [];
        var dealerUrn = RetrievalScope.Current?.DealerUrn ?? query.DealerUrn;
        if (!string.IsNullOrWhiteSpace(dealerUrn))
            nodes = await _graph.TraverseDealerAsync(new DealerUrn(dealerUrn), cancellationToken);

        var mode = RetrievalModeResolver.Resolve(_options);
        if (mode == RetrievalMode.Disabled)
        {
            _logger.LogInformation("RAG disabled correlation {CorrelationId}", query.CorrelationId);
            return new RetrievalResult([], nodes, DateTimeOffset.UtcNow, RouteKind: null);
        }

        var authorization = RetrievalScope.Current
            ?? new RetrievalAuthorization(query.ActorRegion, query.DealerUrn);
        var filter = RetrievalSecurity.Create(
            _options,
            authorization,
            query.DealerUrn,
            query.ActorRegion,
            query.DocumentCategory,
            query.DocumentType);

        var kind = RetrievalQueryRouter.Route(query.Text, mode, _embeddings.IsAvailable || query.Embedding is { Length: > 0 });
        float[]? embedding = query.Embedding;
        if (kind == RetrievalQueryKind.Vector && embedding is not { Length: > 0 })
        {
            if (_embeddings.IsAvailable)
                embedding = await _embeddings.EmbedQueryAsync(query.Text, cancellationToken);
            if (embedding is not { Length: > 0 })
                kind = RetrievalQueryKind.Lexical;
        }

        if (kind == RetrievalQueryKind.Vector && embedding is not { Length: > 0 })
        {
            _logger.LogInformation("Vector retrieval skipped; no query embedding correlation {CorrelationId}", query.CorrelationId);
            return new RetrievalResult([], nodes, DateTimeOffset.UtcNow, RouteKind: kind);
        }

        var request = new DocumentRetrievalRequest(query.Text, embedding, filter, kind, topK);
        var sources = await _retriever.RetrieveAsync(request, cancellationToken);
        var shaped = RetrievalResultShaper.Shape(sources, _options.ClampPromptChunks(), _options.ClampSnippetCharacters());

        _logger.LogInformation(
            "Retrieval complete correlation {CorrelationId} mode {Mode} kind {Kind} sources {SourceCount} graph {GraphCount}",
            query.CorrelationId,
            mode,
            kind,
            shaped.Count,
            nodes.Count);

        return new RetrievalResult(shaped, nodes, DateTimeOffset.UtcNow, RouteKind: kind);
    }
}
