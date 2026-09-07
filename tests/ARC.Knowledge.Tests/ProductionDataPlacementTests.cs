using System.Reflection;
using ARC.Domain.Architecture;
using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Ingestion;

namespace ARC.Knowledge.Tests;

public sealed class ProductionDataPlacementTests
{
    [Fact]
    public void Ingestion_accepts_source_documents_only_not_sql_rows()
    {
        var methods = typeof(IDocumentIngestionService).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(methods, m => m.Name == nameof(IDocumentIngestionService.IngestDocumentAsync));
        Assert.All(methods.SelectMany(m => m.GetParameters()), p =>
        {
            var type = p.ParameterType;
            if (type == typeof(CancellationToken))
                return;
            Assert.True(
                type == typeof(SourceDocument) || type == typeof(IEnumerable<SourceDocument>),
                $"Ingestion must not accept {type.FullName}.");
        });
        Assert.Null(typeof(IDocumentIngestionService).GetMethod("IngestLedgerAsync"));
        Assert.Null(typeof(IDocumentIngestionService).GetMethod("IngestChequeAsync"));
    }

    [Fact]
    public void Grounding_keeps_structured_facts_above_retrieved_text()
    {
        Assert.True((int)EvidenceTrustLevel.AuthoritativeStructured < (int)EvidenceTrustLevel.RetrievedReference);
        Assert.False(ProductionDataPlacement.SemanticMayOverride(
            ProductionDataPlacement.SourcePrecedence.ApprovedSqlStoredProcedure));
    }

    [Fact]
    public void TopK_remains_bounded_and_retrieval_algorithm_is_unchanged()
    {
        Assert.Equal(8, ArcKnowledgeOptions.AssignmentMaxTopK);
        var options = new ArcKnowledgeOptions { MaxRetrievalTopK = 50 };
        Assert.Equal(8, options.ClampTopK(99));
        var ingestion = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "ARC.Knowledge", "Ingestion", "DocumentIngestionService.cs"));
        Assert.Contains("embed-once", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LedgerPosition", ingestion, StringComparison.Ordinal);
        Assert.DoesNotContain("od_os_amt_updt", ingestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EligibilityVerdict", ingestion, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_ingestion_source_does_not_mass_embed_sql_tables()
    {
        var knowledgeRoot = Path.Combine(SolutionRoot(), "src", "ARC.Knowledge");
        foreach (var file in Directory.GetFiles(knowledgeRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("IngestLedger", text, StringComparison.Ordinal);
            Assert.DoesNotContain("EmbedSqlRow", text, StringComparison.Ordinal);
            if (!string.Equals(Path.GetFileName(file), "GraphTraversal.cs", StringComparison.Ordinal))
                Assert.DoesNotContain("ListByDealerAsync", text, StringComparison.Ordinal);
        }

        var graph = File.ReadAllText(Path.Combine(knowledgeRoot, "Graph", "GraphTraversal.cs"));
        Assert.Contains("ILedgerRepository", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateEmbeddings", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("IIndexedDocumentStore", graph, StringComparison.Ordinal);
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ARC.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
