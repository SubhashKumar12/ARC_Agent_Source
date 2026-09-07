namespace ARC.Knowledge.Exceptions;

public class KnowledgeException : Exception
{
    public KnowledgeException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class UnsupportedDocumentException : KnowledgeException
{
    public UnsupportedDocumentException(string documentType)
        : base($"Document Intelligence is not used for '{documentType}'.")
    {
    }
}

public sealed class ExtractionFailedException : KnowledgeException
{
    public ExtractionFailedException(string documentId, Exception? inner = null)
        : base($"Document extraction failed for '{documentId}'.", inner)
    {
    }
}

public sealed class RetrievalFailedException : KnowledgeException
{
    public RetrievalFailedException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
