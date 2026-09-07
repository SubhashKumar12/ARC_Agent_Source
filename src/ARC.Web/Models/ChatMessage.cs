namespace ARC.Web.Models;

public sealed record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    ChatMessageType Type = ChatMessageType.Text);

public enum ChatMessageType
{
    Text,
    WorkflowProgress,
    GateRequest,
    SystemNotification
}

public sealed record WorkflowProgressData(
    string DealerUrn,
    IReadOnlyList<NodeProgressItem> Nodes,
    string Status);

public sealed record NodeProgressItem(
    string NodeName,
    string DisplayName,
    string Status);

public sealed record GateRequestData(
    string GateId,
    string GateName,
    string Reason);

public static class ScenarioMetadata
{
    public static IReadOnlyList<ScenarioInfo> All { get; } = 
    [
        new("S1", "Clean Overdue", "₹1,00,000 overdue, straightforward ODOS notice path"),
        new("S2", "Credit Note Offset", "₹80,000 overdue with ₹30,000 credit note"),
        new("S3", "Section 138 Eligible", "₹2,50,000 overdue with security cheque, legal action path"),
        new("S4", "Mid-Value Overdue", "₹1,50,000 overdue requiring depot approval"),
        new("S5", "Low Value Notice", "₹50,000 overdue, field visit recommended"),
        new("S6", "High Value Litigation", "₹3,00,000 long overdue requiring legal escalation"),
        new("S7", "Standard Recovery", "₹75,000 overdue with standard notice"),
        new("S8", "Complex Exposure", "₹1,80,000 overdue requiring detailed assessment"),
        new("S9", "Regional Priority", "₹1,20,000 overdue for regional priority demo")
    ];
}

public sealed record ScenarioInfo(string Id, string Title, string Description);
