using ARC.Knowledge.Chunking;
using Xunit;

namespace ARC.Knowledge.Tests.Ingestion;

/// <summary>
/// Tests for Phase 2 chunking strategies: policy clauses and template sections.
/// AC: deterministic, idempotent, preserves metadata, respects boundaries.
/// </summary>
public sealed class ChunkingTests
{
    [Fact]
    public void PolicyClauseChunker_ExtractsClausesCorrectly()
    {
        // Arrange
        var chunker = new PolicyClauseChunker();
        var document = new SourceDocument
        {
            SourceDocumentId = "POLICY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = @"1. Introduction
This is the introduction clause.

2. Eligibility Criteria
2.1 First criterion.
2.2 Second criterion.

3. Recovery Process
This describes the recovery process."
        };

        // Act
        var chunks = chunker.ChunkDocument(document);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count >= 3, "Expected at least 3 clauses");
        Assert.All(chunks, chunk =>
        {
            Assert.NotEmpty(chunk.Content);
            Assert.NotEmpty(chunk.PageOrSection);
            Assert.NotEmpty(chunk.Title);
        });
    }

    [Fact]
    public void PolicyClauseChunker_IsDeterministic()
    {
        // Arrange
        var chunker = new PolicyClauseChunker();
        var document = new SourceDocument
        {
            SourceDocumentId = "POLICY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = @"1. Test Clause
This is a test."
        };

        // Act
        var chunks1 = chunker.ChunkDocument(document);
        var chunks2 = chunker.ChunkDocument(document);

        // Assert
        Assert.Equal(chunks1.Count, chunks2.Count);
        for (int i = 0; i < chunks1.Count; i++)
        {
            Assert.Equal(chunks1[i].Content, chunks2[i].Content);
            Assert.Equal(chunks1[i].PageOrSection, chunks2[i].PageOrSection);
            Assert.Equal(chunks1[i].Title, chunks2[i].Title);
        }
    }

    [Fact]
    public void PolicyClauseChunker_RespectsSizeConstraints()
    {
        // Arrange
        var chunker = new PolicyClauseChunker();
        var largeClause = string.Join(" ", Enumerable.Repeat("This is a sentence.", 200));
        var document = new SourceDocument
        {
            SourceDocumentId = "POLICY-002",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = $"1. Large Clause\n{largeClause}"
        };

        // Act
        var chunks = chunker.ChunkDocument(document);

        // Assert
        Assert.All(chunks, chunk =>
        {
            Assert.True(chunk.Content.Length <= 1100, $"Chunk too large: {chunk.Content.Length} chars");
        });
    }

    [Fact]
    public void TemplateChunker_ExtractsSectionsCorrectly()
    {
        // Arrange
        var chunker = new TemplateChunker();
        var document = new SourceDocument
        {
            SourceDocumentId = "TEMPLATE-001",
            DocumentType = "Template",
            DocumentCategory = "NoticeTemplate",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = @"HEADING: Final Demand

DEMAND:
You must pay immediately.

LEGAL NOTICE:
This is a legal requirement.

CLOSING:
Thank you."
        };

        // Act
        var chunks = chunker.ChunkDocument(document);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count >= 3, "Expected at least 3 sections");
        Assert.All(chunks, chunk =>
        {
            Assert.NotEmpty(chunk.Content);
            Assert.NotEmpty(chunk.PageOrSection);
            Assert.NotEmpty(chunk.Title);
        });
    }

    [Fact]
    public void TemplateChunker_IsDeterministic()
    {
        // Arrange
        var chunker = new TemplateChunker();
        var document = new SourceDocument
        {
            SourceDocumentId = "TEMPLATE-001",
            DocumentType = "Template",
            DocumentCategory = "NoticeTemplate",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = @"HEADING: Test
Content here."
        };

        // Act
        var chunks1 = chunker.ChunkDocument(document);
        var chunks2 = chunker.ChunkDocument(document);

        // Assert
        Assert.Equal(chunks1.Count, chunks2.Count);
        for (int i = 0; i < chunks1.Count; i++)
        {
            Assert.Equal(chunks1[i].Content, chunks2[i].Content);
            Assert.Equal(chunks1[i].PageOrSection, chunks2[i].PageOrSection);
        }
    }

    [Fact]
    public void PolicyClauseChunker_SupportsCorrectCategories()
    {
        // Arrange
        var chunker = new PolicyClauseChunker();

        // Act & Assert
        Assert.Contains("RecoveryPolicy", chunker.SupportedCategories);
        Assert.Contains("NoticePolicy", chunker.SupportedCategories);
        Assert.Contains("Policy", chunker.SupportedCategories);
    }

    [Fact]
    public void TemplateChunker_SupportsCorrectCategories()
    {
        // Arrange
        var chunker = new TemplateChunker();

        // Act & Assert
        Assert.Contains("NoticeTemplate", chunker.SupportedCategories);
        Assert.Contains("Template", chunker.SupportedCategories);
    }

    [Fact]
    public void EmptyDocument_ReturnsEmptyChunks()
    {
        // Arrange
        var policyChunker = new PolicyClauseChunker();
        var templateChunker = new TemplateChunker();
        var document = new SourceDocument
        {
            SourceDocumentId = "EMPTY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = ""
        };

        // Act
        var policyChunks = policyChunker.ChunkDocument(document);
        var templateChunks = templateChunker.ChunkDocument(document);

        // Assert
        Assert.Empty(policyChunks);
        Assert.Empty(templateChunks);
    }
}
