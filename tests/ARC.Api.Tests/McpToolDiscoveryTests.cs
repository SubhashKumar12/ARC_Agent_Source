using System.Reflection;
using ARC.Api.Mcp;
using ModelContextProtocol.Server;
using Xunit;

namespace ARC.Api.Tests;

public sealed class McpToolDiscoveryTests
{
    [Fact]
    public void MCP_V1_Tool_Set_Has_Exactly_9_Tools()
    {
        // Arrange
        var toolType = typeof(ArcMcpTools);

        // Act
        var mcpTools = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .ToList();

        // Assert
        Assert.Equal(9, mcpTools.Count);
    }

    [Fact]
    public void MCP_V1_Exposes_Approved_Tools_Only()
    {
        // Arrange
        var approvedTools = new HashSet<string>
        {
            "getDealerDetails",
            "computeNetExposure",
            "checkSection138Eligibility",
            "getLimitationClock",
            "searchDocuments",
            "traverseGraph",
            "verifyDraft",
            "prioritiseRecovery",
            "decideNotice"
        };

        var toolType = typeof(ArcMcpTools);

        // Act
        var mcpTools = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
            .ToHashSet();

        // Assert
        Assert.Equal(approvedTools, mcpTools);
    }

    [Fact]
    public void MCP_V1_Has_No_Write_Tools()
    {
        // Arrange - these are prohibited write tool names
        var prohibitedTools = new HashSet<string>
        {
            "sendNotice", "dispatchVisit", "createLegalCase", "capturePromiseToPay",
            "persistEvidence", "sendCommand", "startWorkflow", "resumeWorkflow"
        };

        var toolType = typeof(ArcMcpTools);

        // Act
        var mcpTools = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
            .ToHashSet();

        // Assert
        Assert.Empty(mcpTools.Intersect(prohibitedTools));
    }

    [Fact]
    public void MCP_V1_Has_No_Gate_Approval_Tools()
    {
        // Arrange - these are prohibited gate approval tool names
        var prohibitedGates = new HashSet<string>
        {
            "approveG1", "approveG2", "approveG3", "approveG4",
            "sendResponse", "resumePort", "completeGate"
        };

        var toolType = typeof(ArcMcpTools);

        // Act
        var mcpTools = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
            .ToHashSet();

        // Assert
        Assert.Empty(mcpTools.Intersect(prohibitedGates));
    }

    [Fact]
    public void All_MCP_Tools_Have_Descriptions()
    {
        // Arrange
        var toolType = typeof(ArcMcpTools);

        // Act
        var toolsWithoutDescriptions = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Where(m => m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() == null)
            .ToList();

        // Assert
        Assert.Empty(toolsWithoutDescriptions);
    }
}
