using ARC.Knowledge.Chunking;
using Xunit;

namespace ARC.Knowledge.Tests.Ingestion;

/// <summary>
/// Tests for content hashing: deterministic, normalized, deduplication.
/// </summary>
public sealed class ContentHashingTests
{
    [Fact]
    public void ComputeHash_IsDeterministic()
    {
        // Arrange
        var content = "This is test content.";

        // Act
        var hash1 = ContentHasher.ComputeHash(content);
        var hash2 = ContentHasher.ComputeHash(content);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    [Fact]
    public void ComputeHash_NormalizesWhitespace()
    {
        // Arrange
        var content1 = "This is   test content.";
        var content2 = "This  is test   content.";
        var content3 = "This\nis\ntest\ncontent.";

        // Act
        var hash1 = ContentHasher.ComputeHash(content1);
        var hash2 = ContentHasher.ComputeHash(content2);
        var hash3 = ContentHasher.ComputeHash(content3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
    }

    [Fact]
    public void ComputeHash_IsCaseInsensitive()
    {
        // Arrange
        var content1 = "This Is Test Content.";
        var content2 = "this is test content.";
        var content3 = "THIS IS TEST CONTENT.";

        // Act
        var hash1 = ContentHasher.ComputeHash(content1);
        var hash2 = ContentHasher.ComputeHash(content2);
        var hash3 = ContentHasher.ComputeHash(content3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
    }

    [Fact]
    public void ComputeHash_DifferentContent_DifferentHash()
    {
        // Arrange
        var content1 = "This is content one.";
        var content2 = "This is content two.";

        // Act
        var hash1 = ContentHasher.ComputeHash(content1);
        var hash2 = ContentHasher.ComputeHash(content2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_EmptyContent_ReturnsEmpty()
    {
        // Act
        var hash1 = ContentHasher.ComputeHash("");
        var hash2 = ContentHasher.ComputeHash(null!);
        var hash3 = ContentHasher.ComputeHash("   ");

        // Assert
        Assert.Empty(hash1);
        Assert.Empty(hash2);
        Assert.Empty(hash3);
    }

    [Fact]
    public void ComputeDeduplicationKey_CombinesHashAndModel()
    {
        // Arrange
        var contentHash = "abc123";
        var embeddingModel = "text-embedding-3-large:3072d";

        // Act
        var key = ContentHasher.ComputeDeduplicationKey(contentHash, embeddingModel);

        // Assert
        Assert.NotEmpty(key);
        Assert.Contains(contentHash, key);
        Assert.Contains("3072d", key.ToLowerInvariant());
    }

    [Fact]
    public void ComputeDeduplicationKey_DifferentModel_DifferentKey()
    {
        // Arrange
        var contentHash = "abc123";
        var model1 = "model1:3072d";
        var model2 = "model2:3072d";

        // Act
        var key1 = ContentHasher.ComputeDeduplicationKey(contentHash, model1);
        var key2 = ContentHasher.ComputeDeduplicationKey(contentHash, model2);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void ComputeDeduplicationKey_EmptyInputs_ReturnsEmpty()
    {
        // Act
        var key1 = ContentHasher.ComputeDeduplicationKey("", "model");
        var key2 = ContentHasher.ComputeDeduplicationKey("hash", "");
        var key3 = ContentHasher.ComputeDeduplicationKey("", "");

        // Assert
        Assert.Empty(key1);
        Assert.Empty(key2);
        Assert.Empty(key3);
    }
}
