using ARC.Agents.Prompts;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Models;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Agents.Tests;

public sealed class WorkflowEdgeTests
{
    [Fact]
    public void Odos_visit_skips_notice_and_issue_opens_g1()
    {
        var visit = Message(tier: RecoveryTier.Visit);
        var notice = Message(tier: RecoveryTier.Notice, decision: NoticeDecision.Issue);
        var reconcile = Message(tier: RecoveryTier.Notice, decision: NoticeDecision.Reconcile, status: WorkflowStatus.Terminated);

        Assert.True(ArcWorkflowEdges.IsVisit(visit));
        Assert.True(ArcWorkflowEdges.IsNotVisit(notice));
        Assert.True(ArcWorkflowEdges.IssuesNotice(notice));
        Assert.True(ArcWorkflowEdges.DoesNotIssueNotice(reconcile));
        Assert.False(ArcWorkflowEdges.IssuesNotice(reconcile));
    }

    [Fact]
    public void Blocked_or_terminated_does_not_continue_running_edge()
    {
        var running = Message(status: WorkflowStatus.Running);
        var blocked = Message(status: WorkflowStatus.Blocked);
        Assert.True(ArcWorkflowEdges.Running(running));
        Assert.True(ArcWorkflowEdges.NotRunning(blocked));
        Assert.True(ArcWorkflowEdges.NotRunning(null));
    }

    [Fact]
    public void Legal_progression_and_advocate_waiting_gates()
    {
        var g3 = Message(waiting: GateId.LegalProgression);
        var g2 = Message(waiting: GateId.AdvocateSignature);
        Assert.True(ArcWorkflowEdges.WaitingLegalProgression(g3));
        Assert.True(ArcWorkflowEdges.NotWaitingLegalProgression(g2));
        Assert.True(ArcWorkflowEdges.WaitingAdvocate(g2));
        Assert.True(ArcWorkflowEdges.NotWaitingAdvocate(g3));
    }

    [Fact]
    public void Missing_verdict_is_not_issue()
    {
        Assert.True(ArcWorkflowEdges.DoesNotIssueNotice(Message()));
        Assert.False(ArcWorkflowEdges.IssuesNotice(Message()));
    }

    private static WorkflowMessage Message(
        WorkflowStatus status = WorkflowStatus.Running,
        RecoveryTier? tier = null,
        NoticeDecision? decision = null,
        GateId? waiting = null)
    {
        var state = new RecoveryState
        {
            CycleId = new CycleId("2026-03"),
            DealerUrn = new DealerUrn("dealer:edge"),
            AsOf = new DateOnly(2026, 3, 1),
            CorrelationId = new CorrelationId("corr-edge"),
            Mode = RunMode.Shadow,
            Status = status
        };
        if (tier is { } t)
            state = state.WithRisk(new RiskAssessment(t, 1m));
        if (decision is { } d)
            state = state.WithNotice(new NoticeVerdict(d, [], []));
        if (waiting is { } gate)
            state = state.WaitingFor(gate);
        return new WorkflowMessage { State = state, Kind = ArcWorkflowKind.Odos };
    }
}
