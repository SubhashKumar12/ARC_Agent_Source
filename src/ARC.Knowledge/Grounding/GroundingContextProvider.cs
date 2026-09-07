using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Exceptions;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Knowledge.Grounding;

/// <summary>
/// Builds purpose-specific grounded context from deterministic graph facts + existing knowledge retrieval.
/// Does not change ranking algorithms and does not call an LLM.
/// </summary>
public interface IGroundingContextProvider
{
    Task<GroundingContext> GetContextAsync(
        GroundingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GroundingContextProvider : IGroundingContextProvider
{
    private readonly ICaseGraphContextProvider _graph;
    private readonly IKnowledgeRetrievalService _retrieval;
    private readonly ArcKnowledgeOptions _options;
    private readonly ILogger<GroundingContextProvider> _logger;

    public GroundingContextProvider(
        ICaseGraphContextProvider graph,
        IKnowledgeRetrievalService retrieval,
        IOptions<ArcKnowledgeOptions> options,
        ILogger<GroundingContextProvider> logger)
    {
        _graph = graph;
        _retrieval = retrieval;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GroundingContext> GetContextAsync(
        GroundingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Reject arbitrary URLs in query text (security boundary).
        if (!string.IsNullOrWhiteSpace(request.QueryText) &&
            request.QueryText.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeException("Arbitrary URLs are not accepted as grounding query input.");
        }

        var profile = GroundingPurposeProfile.For(request.Purpose);
        var topK = _options.ClampTopK(request.TopK);
        var maxPrompt = _options.ClampPromptChunks();
        var utc = DateTimeOffset.UtcNow;

        IReadOnlyList<StructuredFact> facts = Array.Empty<StructuredFact>();
        if (profile.IncludeGraph && !string.IsNullOrWhiteSpace(request.DealerUrn))
        {
            facts = await _graph.GetDealerFactsAsync(
                new CaseGraphContextRequest(
                    request.DealerUrn!,
                    request.CycleId,
                    request.RecoveryCaseId,
                    request.CorrelationId),
                cancellationToken);
        }

        IReadOnlyList<EvidenceSource> knowledge = Array.Empty<EvidenceSource>();
        var queryText = request.QueryText?.Trim() ?? "";
        var canRetrieve = !profile.RequireQueryForKnowledge || queryText.Length > 0;

        if (canRetrieve && queryText.Length > 0)
        {
            // Existing retrieval path: metadata filters + router + shaper (TOP-K / prompt cap).
            var retrieval = await _retrieval.RetrieveAsync(
                new RetrievalQuery(
                    queryText,
                    request.DealerUrn,
                    request.ActorRegion,
                    profile.DocumentCategory,
                    request.CorrelationId,
                    Embedding: null,
                    TopK: topK,
                    DocumentType: profile.DocumentType),
                cancellationToken);

            knowledge = DeduplicateAgainstFacts(retrieval.Sources, facts);
            if (knowledge.Count > maxPrompt)
                knowledge = knowledge.Take(maxPrompt).ToList();
        }

        var citations = BuildCitations(facts, knowledge);
        var insufficient = facts.Count == 0 && knowledge.Count == 0;
        var diagnostics = new GroundingDiagnostics(
            request.Purpose,
            profile.DocumentCategory,
            profile.DocumentType,
            facts.Count,
            knowledge.Count,
            topK,
            maxPrompt,
            insufficient,
            insufficient
                ? "No authorised graph facts or knowledge chunks available."
                : profile.Notes);

        _logger.LogInformation(
            "Grounding purpose {Purpose} correlation {CorrelationId} facts {FactCount} knowledge {KnowledgeCount} insufficient {Insufficient}",
            request.Purpose,
            request.CorrelationId,
            facts.Count,
            knowledge.Count,
            insufficient);

        return new GroundingContext(facts, knowledge, citations, diagnostics, utc);
    }

    private static IReadOnlyList<EvidenceSource> DeduplicateAgainstFacts(
        IReadOnlyList<EvidenceSource> sources,
        IReadOnlyList<StructuredFact> facts)
    {
        var factIds = new HashSet<string>(
            facts.Select(f => f.Provenance.DocumentId),
            StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<EvidenceSource>();

        foreach (var source in sources)
        {
            // Identity/provenance dedupe only — not ranking/MMR.
            if (factIds.Contains(source.Reference.DocumentId))
                continue;

            var key = $"{source.SourceDocumentId ?? source.Reference.DocumentId}|{source.Reference.Version}|{source.Reference.PageOrSection}";
            if (!seenKeys.Add(key))
                continue;

            result.Add(source);
        }

        return result;
    }

    private static IReadOnlyList<SourceReference> BuildCitations(
        IReadOnlyList<StructuredFact> facts,
        IReadOnlyList<EvidenceSource> knowledge)
    {
        var citations = new List<SourceReference>();
        foreach (var fact in facts)
            citations.Add(fact.Provenance);
        foreach (var chunk in knowledge)
            citations.Add(chunk.Reference);
        return citations;
    }
}
