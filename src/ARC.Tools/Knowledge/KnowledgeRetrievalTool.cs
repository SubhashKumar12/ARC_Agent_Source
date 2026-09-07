using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Exceptions;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Knowledge;

public sealed record SearchDocumentsRequest(
    string Text,
    string? DealerUrn,
    string? ActorRegion,
    string? DocumentCategory,
    string? CorrelationId,
    int TopK = 8);

public sealed record TraverseGraphRequest(string DealerUrn, string? CorrelationId);

/// <summary>
/// SearchDocuments + TraverseGraph. Preserves source references. No eligibility decisions.
/// Region and dealer scope are taken from <see cref="RetrievalScope"/> when present.
/// </summary>
public sealed class KnowledgeRetrievalTool
{
    public const string SearchName = "SearchDocuments";
    public const string GraphName = "TraverseGraph";

    private readonly IKnowledgeRetrievalService _retrieval;
    private readonly IGraphTraversal _graph;
    private readonly ILogger<KnowledgeRetrievalTool> _logger;

    public KnowledgeRetrievalTool(
        IKnowledgeRetrievalService retrieval,
        IGraphTraversal graph,
        ILogger<KnowledgeRetrievalTool> logger)
    {
        _retrieval = retrieval;
        _graph = graph;
        _logger = logger;
    }

    public async Task<RetrievalResult> SearchDocumentsAsync(
        SearchDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ToolException(SearchName, "Search text is required.");
        if (request.Text.Contains("://", StringComparison.OrdinalIgnoreCase))
            throw new ToolException(SearchName, "Arbitrary URLs are not accepted as search input.");

        var topK = request.TopK <= 0 ? 8 : Math.Min(request.TopK, 8);
        var authorization = RetrievalScope.Current;
        var dealerUrn = authorization?.DealerUrn ?? request.DealerUrn;
        var actorRegion = authorization?.ActorRegion ?? request.ActorRegion;

        try
        {
            var result = await _retrieval.RetrieveAsync(
                new RetrievalQuery(
                    request.Text.Trim(),
                    dealerUrn,
                    actorRegion,
                    request.DocumentCategory,
                    request.CorrelationId,
                    Embedding: null,
                    TopK: topK),
                cancellationToken);

            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} correlation {CorrelationId} sources {SourceCount} durationMs {DurationMs}",
                SearchName, dealerUrn, request.CorrelationId, result.Sources.Count,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return result;
        }
        catch (KnowledgeException ex)
        {
            throw new ToolException(SearchName, "Knowledge retrieval failed.", ex);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(SearchName, "Knowledge retrieval failed.", ex);
        }
    }

    public async Task<IReadOnlyList<GraphNode>> TraverseGraphAsync(
        TraverseGraphRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var dealerUrn = RetrievalScope.Current?.DealerUrn ?? request.DealerUrn;
        if (string.IsNullOrWhiteSpace(dealerUrn))
            throw new ToolException(GraphName, "DealerUrn is required.");

        try
        {
            var nodes = await _graph.TraverseDealerAsync(new DealerUrn(dealerUrn), cancellationToken);
            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} correlation {CorrelationId} nodes {NodeCount} durationMs {DurationMs}",
                GraphName, dealerUrn, request.CorrelationId, nodes.Count,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            return nodes;
        }
        catch (KnowledgeException ex)
        {
            throw new ToolException(GraphName, "Graph traversal failed.", ex);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(GraphName, "Graph traversal failed.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new ToolException(GraphName, ex.Message, ex);
        }
    }
}
