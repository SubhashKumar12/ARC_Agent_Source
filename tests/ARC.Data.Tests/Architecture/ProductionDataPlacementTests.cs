using ARC.Data.Configuration;
using ARC.Data.Cosmos;

namespace ARC.Data.Tests.Architecture;

public sealed class ProductionDataPlacementTests
{
    [Fact]
    public void Cosmos_defaults_keep_operational_state_separate_from_documents()
    {
        var cosmos = new CosmosStoreOptions();
        Assert.Equal("cycleState", cosmos.CycleStateContainer);
        Assert.Equal("checkpoints", cosmos.CheckpointsContainer);
        Assert.Equal("auditEvents", cosmos.AuditContainer);
        Assert.Equal("conversationState", cosmos.ConversationContainer);
        Assert.Equal("documents", cosmos.DocumentsContainer);
        Assert.Equal(CosmosDocumentsContract.ContainerName, cosmos.DocumentsContainer);
        Assert.Equal("/documentType", CosmosDocumentsContract.PartitionKeyPath);
        var workflow = File.ReadAllText(Path.Combine(SolutionRoot(), "src", "ARC.Data", "Cosmos", "WorkflowStateRepository.cs"));
        Assert.Contains("new PartitionKey(document.cycleId)", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Documents.UpsertItemAsync", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_repositories_do_not_dual_write_rows_into_cosmos_documents()
    {
        var sqlRoot = Path.Combine(SolutionRoot(), "src", "ARC.Data", "Sql");
        foreach (var file in Directory.GetFiles(sqlRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("IIndexedDocumentStore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerateEmbeddingsAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DocumentsContainer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CosmosDocumentsContract", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Blob_defaults_are_evidence_and_legal_worm_not_cosmos()
    {
        var blob = new BlobStoreOptions();
        Assert.Equal("evidence", blob.EvidenceContainer);
        Assert.Equal("legal-worm", blob.LegalContainer);
    }

    [Fact]
    public void Odos_opening_and_limit_remain_sql_stored_procedures_not_embeddings()
    {
        var names = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "ARC.Data", "Sql", "StoredProcedures", "SqlStoredProcedureNames.cs"));
        Assert.Contains("ODOS.usp_GetOpeningDataForArc", names, StringComparison.Ordinal);
        Assert.Contains("ODOS.usp_GetBusinessLineLimit", names, StringComparison.Ordinal);
        Assert.DoesNotContain("embedding", names, StringComparison.OrdinalIgnoreCase);
    }

    private static string SolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ARC.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("Solution root not found");
    }
}
