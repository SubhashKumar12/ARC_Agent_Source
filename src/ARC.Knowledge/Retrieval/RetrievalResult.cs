using ARC.Knowledge.Graph;
using ARC.Knowledge.Provenance;

namespace ARC.Knowledge.Retrieval;

public sealed record RetrievalResult(
    IReadOnlyList<EvidenceSource> Sources,
    IReadOnlyList<GraphNode> GraphNodes,
    DateTimeOffset RetrievedUtc,
    RetrievalQueryKind? RouteKind = null);
