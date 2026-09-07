using Microsoft.Agents.AI.Workflows;
using ARC.Agents.Workflows.Models;
using ARC.Domain.Enums;

namespace ARC.Agents.Workflows;

internal static class ArcWorkflowBind
{
    public static ExecutorBinding Of<TIn, TOut>(
        Func<TIn, IWorkflowContext, CancellationToken, ValueTask<TOut>> handler,
        string id)
        => handler.BindAsExecutor(id, threadsafe: true);
}

internal static class ArcWorkflowEdges
{
    public static bool Running(WorkflowMessage? message)
        => message?.State.Status == WorkflowStatus.Running;

    public static bool NotRunning(WorkflowMessage? message)
        => message is null || message.State.Status != WorkflowStatus.Running;

    public static bool IsVisit(WorkflowMessage? message)
        => message?.State.Risk?.Tier == RecoveryTier.Visit;

    public static bool IsNotVisit(WorkflowMessage? message)
        => message?.State.Risk?.Tier != RecoveryTier.Visit;

    public static bool IssuesNotice(WorkflowMessage? message)
        => message?.State.NoticeVerdict?.Decision == NoticeDecision.Issue;

    public static bool DoesNotIssueNotice(WorkflowMessage? message)
        => message?.State.NoticeVerdict?.Decision != NoticeDecision.Issue;

    public static bool WaitingAdvocate(WorkflowMessage? message)
        => message?.State.WaitingGate == GateId.AdvocateSignature;

    public static bool NotWaitingAdvocate(WorkflowMessage? message)
        => message?.State.WaitingGate != GateId.AdvocateSignature;

    public static bool WaitingLegalProgression(WorkflowMessage? message)
        => message?.State.WaitingGate == GateId.LegalProgression;

    public static bool NotWaitingLegalProgression(WorkflowMessage? message)
        => message?.State.WaitingGate != GateId.LegalProgression;
}
