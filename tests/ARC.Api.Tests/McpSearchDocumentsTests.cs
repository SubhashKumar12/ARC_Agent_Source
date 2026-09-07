using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Mcp;
using ARC.Api.Tests.Fakes;
using ARC.Domain.Enums;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using Xunit;

namespace ARC.Api.Tests;

public sealed class McpSearchDocumentsTests
{
    [Fact]
    public void SearchDocuments_description_is_routed_lexical_or_vector_not_fusion()
    {
        var method = typeof(ArcMcpTools).GetMethod(nameof(ArcMcpTools.SearchDocumentsAsync))!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains("routed lexical-or-vector", description, StringComparison.OrdinalIgnoreCase);
        Assert.False(McpSearchDocumentsContract.DescriptionClaimsFusion(description));
        Assert.False(McpSearchDocumentsContract.DescriptionClaimsFusion(McpSearchDocumentsContract.Description));
    }

    [Fact]
    public void Public_mcp_tool_descriptions_do_not_claim_fusion()
    {
        var descriptions = typeof(ArcMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "");

        Assert.All(descriptions, d => Assert.False(McpSearchDocumentsContract.DescriptionClaimsFusion(d), d));
    }

    [Fact]
    public void Mapper_exposes_citation_fields_and_keeps_missing_values_null()
    {
        var source = new EvidenceSource(
            new SourceReference("kd_1", null, null, null, null, "cosmos-documents", DateTimeOffset.Parse("2026-09-03T10:00:00Z")),
            "Policy",
            "snippet",
            Score: null,
            Status: "ACTIVE",
            RegionScope: "GLOBAL",
            DealerUrn: null,
            SourceDocumentId: "src-1",
            DocumentType: "Policy");

        var hit = McpSearchDocumentsContract.ToHit(source);

        Assert.Equal("kd_1", hit.DocumentId);
        Assert.Equal("src-1", hit.SourceDocumentId);
        Assert.Equal("Policy", hit.DocumentType);
        Assert.Equal("cosmos-documents", hit.SourceSystem);
        Assert.Null(hit.Version);
        Assert.Null(hit.PageOrSection);
        Assert.Null(hit.BlobLocation);
        Assert.Null(hit.Score);
        Assert.Null(hit.DealerUrn);
        Assert.Equal("GLOBAL", hit.RegionScope);
        Assert.Equal(DateTimeOffset.Parse("2026-09-03T10:00:00Z"), hit.RetrievedUtc);
    }

    [Fact]
    public void Lexical_result_does_not_invent_relevance_score()
    {
        var source = new EvidenceSource(
            new SourceReference("kd_lex", "blob://policy", null, "current", "CLAUSE-1", "cosmos-documents", DateTimeOffset.UtcNow),
            "Notice",
            "clause text",
            Score: null,
            Status: "ACTIVE",
            RegionScope: "WEST",
            DealerUrn: "DEALER-NORTH",
            SourceDocumentId: "notice-policy",
            DocumentType: "Policy");

        var hit = McpSearchDocumentsContract.ToHit(source);
        Assert.Null(hit.Score);
        Assert.Equal("kd_lex", hit.DocumentId);
        Assert.Equal("blob://policy", hit.BlobLocation);
        Assert.Equal("current", hit.Version);
        Assert.Equal("CLAUSE-1", hit.PageOrSection);
    }

    [Fact]
    public async Task Authorized_dealer_scope_is_propagated_before_retrieval()
    {
        var knowledge = new CapturingKnowledge();
        var adapter = CreateAdapter(knowledge);
        var actor = new ArcActor("tsi@example.com", ActorRole.Tsi, "North", "DEL");

        var result = await adapter.SearchAsync(actor, "CLAUSE-1", 8, "DEALER-NORTH", "corr", CancellationToken.None);

        Assert.Equal(1, knowledge.Calls);
        Assert.Equal("DEALER-NORTH", knowledge.Last!.DealerUrn);
        Assert.Equal("North", knowledge.Last.ActorRegion);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("DEALER-NORTH", json, StringComparison.Ordinal);
        Assert.Contains("routed lexical-or-vector", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hybrid fusion", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthorized_dealer_search_is_rejected_and_does_not_retrieve()
    {
        var knowledge = new CapturingKnowledge();
        var adapter = CreateAdapter(knowledge);
        var actor = new ArcActor("tsi@example.com", ActorRole.Tsi, "North", "DEL");

        var result = await adapter.SearchAsync(actor, "CLAUSE-1", 8, "DEALER-WEST", "corr", CancellationToken.None);

        Assert.Equal(0, knowledge.Calls);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("forbidden", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceCount", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_dealer_does_not_fall_back_to_unscoped_retrieval()
    {
        var knowledge = new CapturingKnowledge();
        var adapter = CreateAdapter(knowledge);
        var actor = new ArcActor("tsi@example.com", ActorRole.Tsi, "North", "DEL");

        var result = await adapter.SearchAsync(actor, "CLAUSE-1", 8, "DEALER-MISSING", "corr", CancellationToken.None);

        Assert.Equal(0, knowledge.Calls);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("not_found", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Region_isolation_remains_on_unscoped_policy_search()
    {
        var knowledge = new CapturingKnowledge();
        var adapter = CreateAdapter(knowledge);
        var actor = new ArcActor("tsi@example.com", ActorRole.Tsi, "North", "DEL");

        await adapter.SearchAsync(actor, "hold notice dispute", 8, dealerUrn: null, "corr", CancellationToken.None);

        Assert.Equal(1, knowledge.Calls);
        Assert.Null(knowledge.Last!.DealerUrn);
        Assert.Equal("North", knowledge.Last.ActorRegion);
    }

    [Fact]
    public async Task Search_payload_includes_citation_fields_from_sources()
    {
        var retrieved = DateTimeOffset.Parse("2026-09-03T12:00:00Z");
        var knowledge = new CapturingKnowledge
        {
            Sources =
            [
                new EvidenceSource(
                    new SourceReference("kd_cite", "blob://a", null, "current", "CLAUSE-2", "cosmos-documents", retrieved),
                    "Notice",
                    "text",
                    0.12,
                    "ACTIVE",
                    "North",
                    "DEALER-NORTH",
                    "notice-policy",
                    "Policy")
            ],
            RouteKind = RetrievalQueryKind.Vector
        };
        var adapter = CreateAdapter(knowledge);
        var actor = new ArcActor("tsi@example.com", ActorRole.Tsi, "North", "DEL");

        var result = await adapter.SearchAsync(actor, "open dispute hold", 8, "DEALER-NORTH", "corr", CancellationToken.None);
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("kd_cite", json, StringComparison.Ordinal);
        Assert.Contains("notice-policy", json, StringComparison.Ordinal);
        Assert.Contains("cosmos-documents", json, StringComparison.Ordinal);
        Assert.Contains("CLAUSE-2", json, StringComparison.Ordinal);
        Assert.Contains("blob://a", json, StringComparison.Ordinal);
        Assert.Contains("Vector", json, StringComparison.Ordinal);
    }

    private static McpSearchDocumentsAdapter CreateAdapter(CapturingKnowledge knowledge)
    {
        var tool = new KnowledgeRetrievalTool(knowledge, new FakeGraphTraversal(), NullLogger<KnowledgeRetrievalTool>.Instance);
        return new McpSearchDocumentsAdapter(tool, new FakeDealerRepository());
    }

    private sealed class CapturingKnowledge : IKnowledgeRetrievalService
    {
        public int Calls { get; private set; }
        public RetrievalQuery? Last { get; private set; }
        public List<EvidenceSource> Sources { get; init; } = [];
        public RetrievalQueryKind? RouteKind { get; init; } = RetrievalQueryKind.Lexical;

        public Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            Last = query;
            return Task.FromResult(new RetrievalResult(Sources, [], DateTimeOffset.UtcNow, RouteKind));
        }
    }
}
