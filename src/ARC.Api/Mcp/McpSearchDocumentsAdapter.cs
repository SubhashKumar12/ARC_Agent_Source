using ARC.Api.Auth;
using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Knowledge;

namespace ARC.Api.Mcp;

/// <summary>
/// MCP searchDocuments authorization + citation mapping. Authorization happens before retrieval.
/// </summary>
public sealed class McpSearchDocumentsAdapter
{
    private readonly KnowledgeRetrievalTool _knowledge;
    private readonly IDealerRepository _dealers;

    public McpSearchDocumentsAdapter(KnowledgeRetrievalTool knowledge, IDealerRepository dealers)
    {
        _knowledge = knowledge;
        _dealers = dealers;
    }

    public async Task<object> SearchAsync(
        ArcActor actor,
        string query,
        int topK,
        string? dealerUrn,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var searchText = query ?? string.Empty;
        var requestedDealer = string.IsNullOrWhiteSpace(dealerUrn) ? null : dealerUrn.Trim();

        string? authorizedDealer = null;
        if (requestedDealer is not null)
        {
            var denial = await AuthorizeDealerOrDenyAsync(actor, requestedDealer, cancellationToken);
            if (denial is not null)
                return denial;

            authorizedDealer = requestedDealer;
        }

        using var scope = RetrievalScope.Enter(new RetrievalAuthorization(actor.Region, authorizedDealer));
        var request = new SearchDocumentsRequest(
            searchText,
            authorizedDealer,
            actor.Region,
            null,
            correlationId,
            Math.Clamp(topK, 1, 8));

        var result = await _knowledge.SearchDocumentsAsync(request, cancellationToken);
        return McpSearchDocumentsContract.FromRetrieval(result, authorizedDealer, actor.Region);
    }

    private async Task<object?> AuthorizeDealerOrDenyAsync(
        ArcActor actor,
        string dealerUrn,
        CancellationToken cancellationToken)
    {
        var dealer = await _dealers.GetAsync(new DealerUrn(dealerUrn), cancellationToken);
        if (dealer is null)
            return new { error = "Dealer was not found.", code = "not_found" };
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return new { error = "Dealer is outside the actor's region or depot.", code = "forbidden" };
        return null;
    }
}
