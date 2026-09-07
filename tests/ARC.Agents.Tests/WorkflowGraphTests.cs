using System.Reflection;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A1Reconciliation;
using ARC.Agents.A2RiskPrioritisation;
using ARC.Agents.A3NoticeDecisioning;
using ARC.Agents.A4LegalEligibility;
using ARC.Agents.A6FieldOrchestration;
using ARC.Agents.A8SupervisoryInsight;
using ARC.Agents.Tests.Support;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Outbound;

namespace ARC.Agents.Tests;

public sealed class WorkflowGraphTests
{
    [Fact]
    public void AddArcAgents_registers_odos_and_section138_not_a8_on_either_graph()
    {
        using var host = AgentTestHost.Create().Services;
        var odos = host.GetRequiredKeyedService<Workflow>(OdosCycleWorkflow.Name);
        var s138 = host.GetRequiredKeyedService<Workflow>(Section138Workflow.Name);

        Assert.Equal("OdosCycle", odos.Name);
        Assert.Equal("Section138", s138.Name);
        Assert.NotEqual(odos.Name, s138.Name);

        var odosIds = ExecutorIds(odos);
        var s138Ids = ExecutorIds(s138);

        Assert.Contains(ArcWorkflowNodes.A1, odosIds);
        Assert.Contains(ArcWorkflowNodes.A2, odosIds);
        Assert.Contains(ArcWorkflowNodes.A3, odosIds);
        Assert.Contains(ArcWorkflowNodes.A5, odosIds);
        Assert.Contains(ArcWorkflowNodes.A6, odosIds);
        Assert.Contains(ArcWorkflowNodes.ApplyG1, odosIds);
        Assert.Contains(ArcWorkflowNodes.ApplyG2, odosIds);
        Assert.Contains(ArcWorkflowNodes.GateDepotManager, odosIds);
        Assert.Contains(ArcWorkflowNodes.GateAdvocateSignature, odosIds);
        Assert.DoesNotContain(ArcWorkflowNodes.A4, odosIds);
        Assert.DoesNotContain(ArcWorkflowNodes.A7, odosIds);
        Assert.DoesNotContain("A8", odosIds);
        Assert.DoesNotContain(ArcWorkflowNodes.GateLegalProgression, odosIds);

        Assert.Contains(ArcWorkflowNodes.A1, s138Ids);
        Assert.Contains(ArcWorkflowNodes.A4, s138Ids);
        Assert.Contains(ArcWorkflowNodes.A5, s138Ids);
        Assert.Contains(ArcWorkflowNodes.A7, s138Ids);
        Assert.Contains(ArcWorkflowNodes.ApplyG3, s138Ids);
        Assert.Contains(ArcWorkflowNodes.ApplyG2, s138Ids);
        Assert.Contains(ArcWorkflowNodes.ApplyG4, s138Ids);
        Assert.Contains(ArcWorkflowNodes.GateLegalProgression, s138Ids);
        Assert.Contains(ArcWorkflowNodes.GateAdvocateSignature, s138Ids);
        Assert.Contains(ArcWorkflowNodes.GateLegalCaseFileReview, s138Ids);
        Assert.DoesNotContain(ArcWorkflowNodes.A3, s138Ids);
        Assert.DoesNotContain(ArcWorkflowNodes.A6, s138Ids);
        Assert.DoesNotContain("A8", s138Ids);
        Assert.DoesNotContain(ArcWorkflowNodes.GateDepotManager, s138Ids);
    }

    [Fact]
    public void AddArcAgents_registers_a1_through_a8_and_shadow_outbound()
    {
        using var host = AgentTestHost.Create().Services;
        Assert.NotNull(host.GetRequiredService<ReconciliationAgent>());
        Assert.NotNull(host.GetRequiredService<RiskPrioritisationAgent>());
        Assert.NotNull(host.GetRequiredService<NoticeDecisioningAgent>());
        Assert.NotNull(host.GetRequiredService<LegalEligibilityAgent>());
        Assert.NotNull(host.GetRequiredService<FieldOrchestrationAgent>());
        Assert.NotNull(host.GetRequiredService<SupervisoryInsightAgent>());
        Assert.IsType<ShadowOutboundGate>(host.GetRequiredService<IOutboundGate>());
        Assert.Equal("A1-Reconciliation", ReconciliationAgent.Name);
        Assert.Equal("A8-SupervisoryInsight", SupervisoryInsightAgent.Name);
    }

    private static IReadOnlyList<string> ExecutorIds(Workflow workflow)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Walk(workflow, ids, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return [.. ids];
    }

    private static void Walk(object? value, HashSet<string> ids, HashSet<object> seen, int depth)
    {
        if (value is null || depth > 10)
            return;
        var type = value.GetType();
        if (type.IsPrimitive || value is string or decimal or DateTime or DateTimeOffset or DateOnly)
            return;
        if (!type.IsValueType && !seen.Add(value))
            return;

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                Walk(item, ids, seen, depth + 1);
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            object? inner;
            try
            {
                inner = prop.GetValue(value);
            }
            catch
            {
                continue;
            }

            Capture(prop.Name, inner, ids);
            Walk(inner, ids, seen, depth + 1);
        }

        foreach (var field in type.GetFields(flags))
        {
            object? inner;
            try
            {
                inner = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            Capture(field.Name, inner, ids);
            Walk(inner, ids, seen, depth + 1);
        }
    }

    private static void Capture(string member, object? inner, HashSet<string> ids)
    {
        if (inner is not string text || string.IsNullOrWhiteSpace(text))
            return;
        if (member is "Id" or "ExecutorId" or "PortId" or "Name" || member.EndsWith("Id", StringComparison.Ordinal))
            ids.Add(text);
    }
}
