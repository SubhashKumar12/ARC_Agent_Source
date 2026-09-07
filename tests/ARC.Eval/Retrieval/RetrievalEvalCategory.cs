namespace ARC.Eval.Retrieval;

/// <summary>Deterministic retrieval evaluation category for Stage 2 reporting.</summary>
public enum RetrievalEvalCategory
{
    ExactIdentifier,
    SemanticParaphrase,
    Hybrid,
    Policy,
    Template,
    Supervisory,
    MetadataIsolation,
    Negative
}
