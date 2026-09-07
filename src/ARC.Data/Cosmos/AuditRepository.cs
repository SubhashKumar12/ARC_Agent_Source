using Microsoft.Azure.Cosmos;
using ARC.Data.Exceptions;

namespace ARC.Data.Cosmos;

public sealed class AuditRepository : IAuditRepository
{
    private readonly ICosmosClientFactory _cosmos;

    public AuditRepository(ICosmosClientFactory cosmos) => _cosmos = cosmos;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var document = new AuditDocument
        {
            id = $"{auditEvent.OccurredUtc.UtcTicks}:{Guid.NewGuid():N}",
            cycleId = auditEvent.CycleId,
            dealerUrn = auditEvent.DealerUrn,
            eventType = auditEvent.EventType,
            correlationId = auditEvent.CorrelationId,
            occurredUtc = auditEvent.OccurredUtc,
            detail = auditEvent.Detail
        };
        try
        {
            await _cosmos.Audit.CreateItemAsync(document, new PartitionKey(document.cycleId), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to append audit event.", ex);
        }
    }

    private sealed class AuditDocument
    {
        public string id { get; set; } = "";
        public string cycleId { get; set; } = "";
        public string? dealerUrn { get; set; }
        public string eventType { get; set; } = "";
        public string correlationId { get; set; } = "";
        public DateTimeOffset occurredUtc { get; set; }
        public string? detail { get; set; }
    }
}

public static class AuditEventTypes
{
    public const string WorkflowStarted = "workflow.started";
    public const string WorkflowResumed = "workflow.resumed";
    public const string WorkflowBlocked = "workflow.blocked";
    public const string GateRequested = "gate.requested";
    public const string GateApproved = "gate.approved";
    public const string GateDeclined = "gate.declined";
    public const string GateExpired = "gate.expired";
    public const string LegalProgression = "legal.progression";
    public const string EvidenceAdded = "evidence.added";
    public const string ToolExecuted = "tool.executed";
}
