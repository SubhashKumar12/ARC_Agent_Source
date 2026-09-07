namespace ARC.Knowledge.Retrieval;

public enum RetrievalMode
{
    Disabled = 0,
    LexicalOnly = 1,
    VectorOnly = 2,
    Hybrid = 3
}

public enum RetrievalQueryKind
{
    Lexical = 0,
    Vector = 1
}
