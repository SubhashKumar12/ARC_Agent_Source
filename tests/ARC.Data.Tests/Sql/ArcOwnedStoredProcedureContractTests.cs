using System.Text.RegularExpressions;
using ARC.Data.Sql.StoredProcedures;

namespace ARC.Data.Tests.Sql;

/// <summary>
/// Statically verifies Phase 11P ARC-owned SP script matches repository inline-SQL contracts.
/// Does not execute SQL against a database.
/// </summary>
public sealed class ArcOwnedStoredProcedureContractTests
{
    private static readonly string ScriptPath = Path.Combine(
        FindRepoRoot(),
        "SQL", "ARC", "PHASE_11P_ARC_OWNED_STORED_PROCEDURES.sql");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ARC.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (ARC.sln).");
    }

    public static TheoryData<ArcOwnedSpContract> Contracts => new(ArcOwnedSpContractCatalog.All);

    [Fact]
    public void Phase11P_Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected script at {ScriptPath}");
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Phase11P_Procedure_Exists_InScript(ArcOwnedSpContract contract)
    {
        var script = File.ReadAllText(ScriptPath);
        Assert.Matches(
            new Regex($@"CREATE\s+OR\s+ALTER\s+PROCEDURE\s+{Regex.Escape(contract.ProcedureName)}\b", RegexOptions.IgnoreCase),
            script);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Phase11P_Procedure_IsAllowListed(ArcOwnedSpContract contract)
    {
        var names = new SqlStoredProcedureNames();
        var allowList = typeof(SqlStoredProcedureNames)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(names))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(contract.ProcedureName, allowList);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Phase11P_Parameters_Present_InScript(ArcOwnedSpContract contract)
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, contract.ProcedureName);
        Assert.NotNull(body);

        foreach (var param in contract.Parameters)
        {
            Assert.Contains(param, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Phase11P_OutputColumns_Present_InScript(ArcOwnedSpContract contract)
    {
        if (contract.OutputColumns.Length == 0)
            return;

        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, contract.ProcedureName);
        Assert.NotNull(body);

        foreach (var column in contract.OutputColumns)
        {
            Assert.Contains(column, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Phase11P_GateDecision_Save_Preserves_IdempotentInsert()
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, "dbo.usp_SaveGateDecision");
        Assert.NotNull(body);
        Assert.Contains("IF NOT EXISTS", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CycleId = @CycleId AND DealerUrn = @DealerUrn", body, StringComparison.Ordinal);
        Assert.Contains("GateId = @GateId AND CorrelationId = @CorrelationId", body, StringComparison.Ordinal);
        Assert.Contains("WasOverride", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase11P_SaveResolvedMapping_Preserves_Throw50001()
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, "dbo.usp_SaveResolvedMapping");
        Assert.NotNull(body);
        Assert.Contains("THROW 50001", body, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase11P_ListDueConfirmed_Preserves_BrokenPtpExclusion()
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, "dbo.usp_ListPtpCandidatesByStatus");
        Assert.NotNull(body);
        Assert.Contains("ChaseType = N'Broken'", body, StringComparison.Ordinal);
        Assert.Contains("ORDER BY p.CommitmentDate, p.RecordId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase11P_RankedWorklist_Preserves_TopDecilePredicate()
    {
        var script = File.ReadAllText(ScriptPath);
        var body = ExtractProcedureBody(script, "dbo.usp_GetRankedWorklist");
        Assert.NotNull(body);
        Assert.Contains("Status NOT IN (N'Blocked', N'Failed')", body, StringComparison.Ordinal);
        Assert.Contains("Latin1_General_BIN2", body, StringComparison.Ordinal);
        Assert.Contains("@TopDecile = 0", body, StringComparison.Ordinal);
    }

    private static string? ExtractProcedureBody(string script, string procedureName)
    {
        var pattern = $@"CREATE\s+OR\s+ALTER\s+PROCEDURE\s+{Regex.Escape(procedureName)}\b[\s\S]*?(?=^GO\s*$|\z)";
        var match = Regex.Match(script, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Value : null;
    }
}

public sealed record ArcOwnedSpContract(
    string Repository,
    string Method,
    string ConfigProperty,
    string ProcedureName,
    string[] Parameters,
    string[] OutputColumns,
    string SourceFile);

public static class ArcOwnedSpContractCatalog
{
    private static readonly string[] PtpRecordColumns =
    [
        "RecordId", "CycleId", "DealerUrn", "CommitmentDate", "Amount", "Currency", "Status",
        "ConfirmedByTsi", "ConfirmedUtc", "ConfirmedByUpn", "RequiresTsiConfirmation", "Discarded",
        "Locale", "SpeechConfidence", "TranscriptSha256", "RecognitionStatus", "CorrelationId",
        "CreatedUtc", "UpdatedUtc"
    ];

    private static readonly string[] PtpChaseColumns =
    [
        "ChaseId", "PtpId", "CycleId", "DealerUrn", "OwnerTsi", "CommitmentDate", "DueDate",
        "ChaseType", "Status", "CorrelationId", "CreatedUtc"
    ];

    private static readonly string[] RecoveryIndexColumns =
    [
        "CycleId", "DealerUrn", "Status", "CorrelationId", "WaitingGate", "UpdatedUtc",
        "RecoverabilityScore", "RecoveryTier"
    ];

    public static IReadOnlyList<ArcOwnedSpContract> All { get; } =
    [
        new("RecoveryCaseRepository", "UpsertIndexAsync", nameof(SqlStoredProcedureNames.UpsertRecoveryCaseIndex),
            "dbo.usp_UpsertRecoveryCaseIndex",
            ["@CycleId", "@DealerUrn", "@Status", "@CorrelationId", "@WaitingGate", "@UpdatedUtc", "@RecoverabilityScore", "@RecoveryTier"],
            [], "RecoveryCaseRepository.cs"),
        new("RecoveryCaseRepository", "GetAsync", nameof(SqlStoredProcedureNames.GetRecoveryCaseIndex),
            "dbo.usp_GetRecoveryCaseIndex",
            ["@CycleId", "@DealerUrn"], RecoveryIndexColumns, "RecoveryCaseRepository.cs"),
        new("RecoveryCaseRepository", "ListByCycleAsync", nameof(SqlStoredProcedureNames.ListRecoveryCasesByCycle),
            "dbo.usp_ListRecoveryCasesByCycle",
            ["@CycleId", "@Region", "@Depot"], RecoveryIndexColumns, "RecoveryCaseRepository.cs"),
        new("RecoveryCaseRepository", "ListRankedWorklistAsync", nameof(SqlStoredProcedureNames.GetRankedWorklist),
            "dbo.usp_GetRankedWorklist",
            ["@CycleId", "@Region", "@Depot", "@TopDecile"],
            ["DealerUrn", "RecoverabilityScore", "RecoveryTier", "Status", "WaitingGate", "RankNo", "EligibleCount"],
            "RecoveryCaseRepository.cs"),
        new("DealerIdentityMappingRepository", "GetMappingAsync", nameof(SqlStoredProcedureNames.GetDealerSourceMapping),
            "dbo.usp_GetDealerSourceMapping",
            ["@SourceSystem", "@SourceIdentifier"],
            ["SourceSystem", "SourceIdentifier", "CanonicalUrn", "MatchKind"], "DealerIdentityMappingRepository.cs"),
        new("DealerIdentityMappingRepository", "SaveResolvedMappingAsync", nameof(SqlStoredProcedureNames.SaveResolvedMapping),
            "dbo.usp_SaveResolvedMapping",
            ["@SourceSystem", "@SourceIdentifier", "@CanonicalUrn", "@MatchKind"], [], "DealerIdentityMappingRepository.cs"),
        new("DealerIdentityMappingRepository", "FindDealerUrnsByIdentifierAsync", nameof(SqlStoredProcedureNames.FindDealerUrnsByIdentifier),
            "dbo.usp_FindDealerUrnsByIdentifier",
            ["@SourceSystem", "@SourceIdentifier"], ["Urn"], "DealerIdentityMappingRepository.cs"),
        new("DealerIdentityMappingRepository", "FindDealerUrnsByAliasValueAsync", nameof(SqlStoredProcedureNames.FindDealerUrnsByAlias),
            "dbo.usp_FindDealerUrnsByAlias",
            ["@AliasValue"], ["CanonicalUrn"], "DealerIdentityMappingRepository.cs"),
        new("DealerIdentityMappingRepository", "SaveAliasAsync", nameof(SqlStoredProcedureNames.SaveDealerAlias),
            "dbo.usp_SaveDealerAlias",
            ["@CanonicalUrn", "@AliasKind", "@AliasValue"], [], "DealerIdentityMappingRepository.cs"),
        new("GateDecisionRepository", "SaveAsync", nameof(SqlStoredProcedureNames.SaveGateDecision),
            "dbo.usp_SaveGateDecision",
            ["@CycleId", "@DealerUrn", "@GateId", "@ActorUpn", "@ActorRole", "@Decision", "@Reason", "@RecommendedAction", "@DecidedUtc", "@CorrelationId", "@WasOverride"],
            [], "GateDecisionRepository.cs"),
        new("GateDecisionRepository", "ListAsync", nameof(SqlStoredProcedureNames.ListGateDecisions),
            "dbo.usp_ListGateDecisions",
            ["@CycleId", "@DealerUrn"],
            ["GateId", "ActorUpn", "ActorRole", "Decision", "Reason", "RecommendedAction", "DecidedUtc", "CorrelationId"],
            "GateDecisionRepository.cs"),
        new("SqlPtpRecordRepository", "UpsertAsync", nameof(SqlStoredProcedureNames.UpsertPtpRecord),
            "dbo.usp_UpsertPtpRecord",
            ["@RecordId", "@CycleId", "@DealerUrn", "@CommitmentDate", "@Amount", "@Currency", "@Status", "@ConfirmedByTsi", "@CreatedUtc", "@UpdatedUtc"],
            [], "SqlPtpRecordRepository.cs"),
        new("SqlPtpRecordRepository", "GetAsync", nameof(SqlStoredProcedureNames.GetPtpRecord),
            "dbo.usp_GetPtpRecord", ["@RecordId"], PtpRecordColumns, "SqlPtpRecordRepository.cs"),
        new("SqlPtpRecordRepository", "ListCommittedByCycleAsync", nameof(SqlStoredProcedureNames.ListPtpCandidatesByCycleDealer),
            "dbo.usp_ListPtpCandidatesByCycleDealer",
            ["@CycleId"], PtpRecordColumns, "SqlPtpRecordRepository.cs"),
        new("SqlPtpRecordRepository", "ListCommittedByDealerAsync", nameof(SqlStoredProcedureNames.ListPtpCommittedByDealer),
            "dbo.usp_ListPtpCommittedByDealer",
            ["@DealerUrn"], PtpRecordColumns, "SqlPtpRecordRepository.cs"),
        new("SqlPtpRecordRepository", "ListDueConfirmedAsync", nameof(SqlStoredProcedureNames.ListPtpCandidatesByStatus),
            "dbo.usp_ListPtpCandidatesByStatus",
            ["@AsOf"], PtpRecordColumns, "SqlPtpRecordRepository.cs"),
        new("SqlVisitPlanRepository", "UpsertAsync", nameof(SqlStoredProcedureNames.UpsertVisitPlan),
            "dbo.usp_UpsertVisitPlan",
            ["@PlanId", "@CycleId", "@TsiId", "@PlanDate", "@CorrelationId", "@CreatedUtc", "@UpdatedUtc", "@Lines"],
            [], "SqlVisitPlanRepository.cs"),
        new("SqlVisitPlanRepository", "GetAsync", nameof(SqlStoredProcedureNames.GetVisitPlan),
            "dbo.usp_GetVisitPlan",
            ["@PlanId"],
            ["PlanId", "CycleId", "TsiId", "PlanDate", "DealerUrn", "Sequence", "PriorityRank"],
            "SqlVisitPlanRepository.cs"),
        new("SqlVisitPlanRepository", "ListByCycleAsync", nameof(SqlStoredProcedureNames.ListVisitPlansByCycleTsi),
            "dbo.usp_ListVisitPlansByCycleTsi",
            ["@CycleId"],
            ["PlanId", "CycleId", "TsiId", "PlanDate", "DealerUrn", "Sequence"],
            "SqlVisitPlanRepository.cs"),
        new("SqlPtpChaseRepository", "TryInsertAsync", nameof(SqlStoredProcedureNames.UpsertPtpChase),
            "dbo.usp_UpsertPtpChase",
            ["@ChaseId", "@PtpId", "@CycleId", "@DealerUrn", "@OwnerTsi", "@CommitmentDate", "@DueDate", "@ChaseType", "@Status", "@CorrelationId", "@CreatedUtc"],
            [], "SqlPtpChaseRepository.cs"),
        new("SqlPtpChaseRepository", "GetAsync", nameof(SqlStoredProcedureNames.GetPtpChase),
            "dbo.usp_GetPtpChase", ["@ChaseId"], PtpChaseColumns, "SqlPtpChaseRepository.cs"),
        new("SqlPtpChaseRepository", "ListByCycleAsync", nameof(SqlStoredProcedureNames.ListPtpChasesByCycleStatus),
            "dbo.usp_ListPtpChasesByCycleStatus",
            ["@CycleId"], PtpChaseColumns, "SqlPtpChaseRepository.cs"),
        new("SqlPtpChaseRepository", "ListByStatusAsync", nameof(SqlStoredProcedureNames.ListPtpChasesByTsiStatus),
            "dbo.usp_ListPtpChasesByTsiStatus",
            ["@Status"], PtpChaseColumns, "SqlPtpChaseRepository.cs"),
    ];
}
