using ARC.Knowledge.Chunking;
using ARC.Knowledge.Ingestion;

namespace ARC.Cli.Commands;

/// <summary>
/// Optional CLI command for testing document ingestion. Not part of S1-S9 scenarios.
/// </summary>
public sealed class IngestionCommand
{
    private readonly IDocumentIngestionService _ingestionService;

    public IngestionCommand(IDocumentIngestionService ingestionService)
    {
        _ingestionService = ingestionService;
    }

    /// <summary>
    /// Runs ingestion test with sample policy/template documents.
    /// Shows counts only, never dumps embeddings or secrets.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("ARC Knowledge Ingestion Test");
        Console.WriteLine("============================");
        Console.WriteLine();

        var sampleDocuments = CreateSampleDocuments();

        Console.WriteLine($"Ingesting {sampleDocuments.Count} sample documents...");
        Console.WriteLine();

        var result = await _ingestionService.IngestDocumentsAsync(sampleDocuments, cancellationToken);

        Console.WriteLine("Ingestion Complete");
        Console.WriteLine("==================");
        Console.WriteLine($"Documents processed: {result.DocumentsProcessed}");
        Console.WriteLine($"Chunks created: {result.ChunksProduced}");
        Console.WriteLine($"Chunks embedded: {result.ChunksEmbedded}");
        Console.WriteLine($"Chunks unchanged: {result.ChunksSkippedUnchanged}");
        Console.WriteLine($"Chunks failed: {result.ChunksFailed}");
        Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F2}s");
        Console.WriteLine();

        if (result.Errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
            Console.WriteLine();
        }

        return result.IsSuccess ? 0 : 1;
    }

    private static List<SourceDocument> CreateSampleDocuments()
    {
        return new List<SourceDocument>
        {
            new()
            {
                SourceDocumentId = "POLICY-RECOVERY-001",
                DocumentType = "Policy",
                DocumentCategory = "RecoveryPolicy",
                Status = "ACTIVE",
                Version = "current",
                RegionScope = new[] { "GLOBAL" },
                BlobLocation = "policies/recovery-001.pdf",
                Content = @"1. Introduction
This policy establishes the framework for recovery of outstanding dealer balances.

2. Eligibility Criteria
2.1 Recovery action may be initiated when a dealer balance exceeds the threshold for more than 90 days.
2.2 The dealer must have been notified at least twice in writing.

3. Recovery Process
3.1 Initial contact shall be made by the recovery team.
3.2 A payment plan may be negotiated if the dealer demonstrates willingness to pay.

4. Legal Action
If recovery efforts fail, legal action may be initiated in accordance with Section 138 procedures."
            },
            new()
            {
                SourceDocumentId = "TEMPLATE-NOTICE-001",
                DocumentType = "Template",
                DocumentCategory = "NoticeTemplate",
                Status = "ACTIVE",
                Version = "current",
                RegionScope = new[] { "GLOBAL" },
                BlobLocation = "templates/notice-001.html",
                Content = @"SUBJECT: Final Demand Notice

DEMAND:
This is to inform you that your account shows an outstanding balance. 
Immediate payment is required to avoid further action.

LEGAL NOTICE:
Failure to settle this amount may result in legal proceedings under Section 138 of the Negotiable Instruments Act.

PAYMENT INSTRUCTIONS:
Please remit payment to the address below within 15 days of receipt of this notice.

CLOSING:
For queries, contact our recovery team."
            }
        };
    }
}
