using Microsoft.Agents.AI.Workflows;
using ARC.Agents.Workflows.Executors;
using ARC.Agents.Workflows.Models;

namespace ARC.Agents.Workflows;

/// <summary>Workflow A — monthly ODOS demand-notice cycle.</summary>
public sealed class OdosCycleWorkflow
{
    public const string Name = "OdosCycle";

    private readonly ArcWorkflowExecutors _executors;

    public OdosCycleWorkflow(ArcWorkflowExecutors executors) => _executors = executors;

    public Workflow Build()
    {
        var a1 = ArcWorkflowBind.Of<WorkflowRunRequest, WorkflowMessage>(_executors.A1Async, ArcWorkflowNodes.A1);
        var a2 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A2Async, ArcWorkflowNodes.A2);
        var a3 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A3Async, ArcWorkflowNodes.A3);
        var a5 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A5Async, ArcWorkflowNodes.A5);
        var a6 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A6Async, ArcWorkflowNodes.A6);
        var terminate = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.TerminateAsync, ArcWorkflowNodes.Terminate);
        var applyG1 = ArcWorkflowBind.Of<GateApprovalResponse, WorkflowMessage>(_executors.ApplyG1Async, ArcWorkflowNodes.ApplyG1);
        var applyG2 = ArcWorkflowBind.Of<GateApprovalResponse, WorkflowMessage>(_executors.ApplyG2Async, ArcWorkflowNodes.ApplyG2);

        var g1 = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(ArcWorkflowNodes.GateDepotManager).BindAsExecutor();
        var g2 = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(ArcWorkflowNodes.GateAdvocateSignature).BindAsExecutor();

        return new WorkflowBuilder(a1)
            .WithName(Name)
            .WithDescription("PaintCo ODOS demand-notice cycle. Human gates G1 and G2. Expiry is not approval.")
            .AddEdge<WorkflowMessage>(a1, a2, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(a1, terminate, ArcWorkflowEdges.NotRunning)
            .AddEdge<WorkflowMessage>(a2, a6, ArcWorkflowEdges.IsVisit)
            .AddEdge<WorkflowMessage>(a2, a3, ArcWorkflowEdges.IsNotVisit)
            .AddEdge<WorkflowMessage>(a3, g1, ArcWorkflowEdges.IssuesNotice)
            .AddEdge<WorkflowMessage>(a3, terminate, ArcWorkflowEdges.DoesNotIssueNotice)
            .AddEdge(g1, applyG1)
            .AddEdge<WorkflowMessage>(applyG1, a5, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(applyG1, terminate, ArcWorkflowEdges.NotRunning)
            .AddEdge<WorkflowMessage>(a5, g2, ArcWorkflowEdges.WaitingAdvocate)
            .AddEdge<WorkflowMessage>(a5, terminate, ArcWorkflowEdges.NotWaitingAdvocate)
            .AddEdge(g2, applyG2)
            .AddEdge<WorkflowMessage>(applyG2, a6, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(applyG2, terminate, ArcWorkflowEdges.NotRunning)
            .WithOutputFrom(a6, terminate)
            .Build();
    }
}
