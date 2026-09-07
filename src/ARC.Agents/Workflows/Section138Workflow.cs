using Microsoft.Agents.AI.Workflows;
using ARC.Agents.Workflows.Executors;
using ARC.Agents.Workflows.Models;

namespace ARC.Agents.Workflows;

/// <summary>Workflow B — Section 138 legal pipeline after demand notice + bounce + 60 days.</summary>
public sealed class Section138Workflow
{
    public const string Name = "Section138";

    private readonly ArcWorkflowExecutors _executors;

    public Section138Workflow(ArcWorkflowExecutors executors) => _executors = executors;

    public Workflow Build()
    {
        var a1 = ArcWorkflowBind.Of<WorkflowRunRequest, WorkflowMessage>(_executors.A1Async, ArcWorkflowNodes.A1);
        var a4 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A4Async, ArcWorkflowNodes.A4);
        var a5 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A5Async, ArcWorkflowNodes.A5);
        var a7 = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.A7Async, ArcWorkflowNodes.A7);
        var terminate = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.TerminateAsync, ArcWorkflowNodes.Terminate);
        var complete = ArcWorkflowBind.Of<WorkflowMessage, WorkflowMessage>(_executors.CompleteAsync, ArcWorkflowNodes.Complete);
        var applyG2 = ArcWorkflowBind.Of<GateApprovalResponse, WorkflowMessage>(_executors.ApplyG2Async, ArcWorkflowNodes.ApplyG2);
        var applyG3 = ArcWorkflowBind.Of<GateApprovalResponse, WorkflowMessage>(_executors.ApplyG3Async, ArcWorkflowNodes.ApplyG3);
        var applyG4 = ArcWorkflowBind.Of<GateApprovalResponse, WorkflowMessage>(_executors.ApplyG4Async, ArcWorkflowNodes.ApplyG4);

        var g2 = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(ArcWorkflowNodes.GateAdvocateSignature).BindAsExecutor();
        var g3 = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(ArcWorkflowNodes.GateLegalProgression).BindAsExecutor();
        var g4 = RequestPort.Create<WorkflowMessage, GateApprovalResponse>(ArcWorkflowNodes.GateLegalCaseFileReview).BindAsExecutor();

        return new WorkflowBuilder(a1)
            .WithName(Name)
            .WithDescription("PaintCo Section 138 pipeline. Human gates G3, G2, G4. Court filing is out of scope.")
            .AddEdge<WorkflowMessage>(a1, a4, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(a1, terminate, ArcWorkflowEdges.NotRunning)
            .AddEdge<WorkflowMessage>(a4, g3, ArcWorkflowEdges.WaitingLegalProgression)
            .AddEdge<WorkflowMessage>(a4, terminate, ArcWorkflowEdges.NotWaitingLegalProgression)
            .AddEdge(g3, applyG3)
            .AddEdge<WorkflowMessage>(applyG3, a5, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(applyG3, terminate, ArcWorkflowEdges.NotRunning)
            .AddEdge<WorkflowMessage>(a5, g2, ArcWorkflowEdges.WaitingAdvocate)
            .AddEdge<WorkflowMessage>(a5, terminate, ArcWorkflowEdges.NotWaitingAdvocate)
            .AddEdge(g2, applyG2)
            .AddEdge<WorkflowMessage>(applyG2, a7, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(applyG2, terminate, ArcWorkflowEdges.NotRunning)
            .AddEdge(a7, g4)
            .AddEdge(g4, applyG4)
            .AddEdge<WorkflowMessage>(applyG4, complete, ArcWorkflowEdges.Running)
            .AddEdge<WorkflowMessage>(applyG4, terminate, ArcWorkflowEdges.NotRunning)
            .WithOutputFrom(complete, terminate)
            .Build();
    }
}
