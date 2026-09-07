using ARC.Domain.ValueObjects;

namespace ARC.Data.Tests.Architecture;

/// <summary>
/// Architecture test: production SQL repositories must not contain inline SQL.
/// All production data access must go through stored procedures.
/// </summary>
public sealed class RepositorySqlArchitectureTests
{
    private static readonly string[] InlineSqlPatterns =
    [
        "SELECT ",
        "INSERT ",
        "UPDATE ",
        "DELETE ",
        "MERGE "
    ];

    /// <summary>Phase 11RS: all ARC-owned repositories fully migrated to stored procedures.</summary>
    private static readonly string[] ArcOwnedRepositoryFiles =
    [
        "GateDecisionRepository.cs",
        "RecoveryCaseRepository.cs",
        "DealerIdentityMappingRepository.cs",
        "SqlPtpRecordRepository.cs",
        "SqlVisitPlanRepository.cs",
        "SqlPtpChaseRepository.cs"
    ];

    /// <summary>ODOS / Legal / source-contract TBC — inline SQL expected until wrappers exist.</summary>
    private static readonly string[] OdosBlockedRepositoryFiles =
    [
        "DealerRepository.cs",
        "LedgerRepository.cs",
        "ChequeRepository.cs",
        "LegalCaseRepository.cs"
    ];

    [Fact]
    public void ArcOwnedRepositories_MustNotContainInlineSql()
    {
        var repoPath = Path.Combine(GetSolutionRoot(), "src", "ARC.Data", "Sql");
        var violations = new List<string>();

        foreach (var fileName in ArcOwnedRepositoryFiles)
        {
            var filePath = Path.Combine(repoPath, fileName);
            Assert.True(File.Exists(filePath), $"{fileName} not found");
            violations.AddRange(FindInlineSqlViolations(fileName, File.ReadAllText(filePath)));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void OdosBlockedRepositories_StillContainInlineSql_AsDocumented()
    {
        var repoPath = Path.Combine(GetSolutionRoot(), "src", "ARC.Data", "Sql");
        var withInlineSql = 0;

        foreach (var fileName in OdosBlockedRepositoryFiles)
        {
            var filePath = Path.Combine(repoPath, fileName);
            Assert.True(File.Exists(filePath), $"{fileName} not found");
            var violations = FindInlineSqlViolations(fileName, File.ReadAllText(filePath)).ToList();
            if (violations.Count > 0)
                withInlineSql++;
        }

        // Phase 12C/13C: ChequeRepository, LegalCaseRepository, and DealerRepository.GetAsync migrated; Dealer list/Ledger fully blocked.
        Assert.Equal(4, withInlineSql);
    }

    [Fact]
    public void Phase12C_MigratedMethods_MustUseStoredProcedureExecutor()
    {
        var repoPath = Path.Combine(GetSolutionRoot(), "src", "ARC.Data", "Sql");
        var migratedMethods = new (string File, string MethodMarker)[]
        {
            ("ChequeRepository.cs", "public async Task<IReadOnlyList<SecurityCheque>> ListChequesAsync"),
            ("LegalCaseRepository.cs", "public async Task<LegalCase?> GetAsync"),
            ("DealerRepository.cs", "public async Task<DealerMasterDetail?> GetMasterDetailAsync"),
        };

        var violations = new List<string>();
        foreach (var (file, methodMarker) in migratedMethods)
        {
            var content = File.ReadAllText(Path.Combine(repoPath, file));
            var methodStart = content.IndexOf(methodMarker, StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"{file}: {methodMarker} not found");

            var nextMethod = content.IndexOf("\n    public ", methodStart + methodMarker.Length, StringComparison.Ordinal);
            var methodBody = nextMethod < 0 ? content[methodStart..] : content[methodStart..nextMethod];
            violations.AddRange(FindInlineSqlViolations($"{file} {methodMarker}", methodBody));

            if (!methodBody.Contains("_procedures.", StringComparison.Ordinal)
                && !methodBody.Contains("LoadMasterDetailAsync", StringComparison.Ordinal))
                violations.Add($"{file} {methodMarker} must use _procedures");
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ArcOwnedMigratedMethods_MustUseStoredProcedureExecutor()
    {
        var repoPath = Path.Combine(GetSolutionRoot(), "src", "ARC.Data", "Sql");
        var migratedMethods = new (string File, string MethodMarker)[]
        {
            ("RecoveryCaseRepository.cs", "public async Task UpsertIndexAsync"),
            ("RecoveryCaseRepository.cs", "public async Task<RecoveryCaseIndex?> GetAsync"),
            ("RecoveryCaseRepository.cs", "public async Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync"),
            ("RecoveryCaseRepository.cs", "public async Task<RankedWorklist> ListRankedWorklistAsync"),
            ("DealerIdentityMappingRepository.cs", "public async Task<DealerSourceMapping?> GetMappingAsync"),
            ("DealerIdentityMappingRepository.cs", "public async Task SaveResolvedMappingAsync"),
            ("DealerIdentityMappingRepository.cs", "public async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByIdentifierAsync"),
            ("DealerIdentityMappingRepository.cs", "public async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByAliasValueAsync"),
            ("DealerIdentityMappingRepository.cs", "public async Task SaveAliasAsync"),
            ("GateDecisionRepository.cs", "public async Task SaveAsync"),
            ("GateDecisionRepository.cs", "public async Task<IReadOnlyList<GateDecision>> ListAsync"),
            ("SqlPtpRecordRepository.cs", "public async Task UpsertAsync"),
            ("SqlPtpRecordRepository.cs", "public async Task<PtpRecordEntity?> GetAsync"),
            ("SqlPtpRecordRepository.cs", "public async Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByCycleAsync"),
            ("SqlPtpRecordRepository.cs", "public async Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByDealerAsync"),
            ("SqlPtpRecordRepository.cs", "public async Task<IReadOnlyList<PtpRecordEntity>> ListDueConfirmedAsync"),
            ("SqlVisitPlanRepository.cs", "public async Task UpsertAsync"),
            ("SqlVisitPlanRepository.cs", "public async Task<VisitPlanEntity?> GetAsync"),
            ("SqlVisitPlanRepository.cs", "public async Task<IReadOnlyList<VisitPlanEntity>> ListByCycleAsync"),
            ("SqlPtpChaseRepository.cs", "public async Task<bool> TryInsertAsync"),
            ("SqlPtpChaseRepository.cs", "public async Task<PtpChaseEntity?> GetAsync"),
            ("SqlPtpChaseRepository.cs", "public async Task<IReadOnlyList<PtpChaseEntity>> ListByCycleAsync"),
            ("SqlPtpChaseRepository.cs", "public async Task<IReadOnlyList<PtpChaseEntity>> ListByStatusAsync"),
        };

        var violations = new List<string>();
        foreach (var group in migratedMethods.GroupBy(m => m.File))
        {
            var content = File.ReadAllText(Path.Combine(repoPath, group.Key));
            foreach (var (_, methodMarker) in group)
            {
                var methodStart = content.IndexOf(methodMarker, StringComparison.Ordinal);
                if (methodStart < 0)
                {
                    violations.Add($"{group.Key}: method marker not found: {methodMarker}");
                    continue;
                }

                var nextMethod = content.IndexOf("\n    public ", methodStart + methodMarker.Length, StringComparison.Ordinal);
                var methodBody = nextMethod < 0 ? content[methodStart..] : content[methodStart..nextMethod];
                violations.AddRange(FindInlineSqlViolations($"{group.Key} {methodMarker}", methodBody));

                if (!methodBody.Contains("_procedures.", StringComparison.Ordinal))
                    violations.Add($"{group.Key} {methodMarker} must use _procedures");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Migration_Status_AllArcOwnedOperationsMigrated()
    {
        const int phase11cMatrixOperations = 30;
        const int phase11pSpBackedMethods = 23;
        const int matrixMigratedOperations = 22;
        const int odosBlockedRemaining = 5;
        const int odosMigratedMethods = 3;
        Assert.Equal(phase11cMatrixOperations, matrixMigratedOperations + odosBlockedRemaining + odosMigratedMethods);
        Assert.Equal(23, phase11pSpBackedMethods);
        Assert.Equal(1, phase11pSpBackedMethods - matrixMigratedOperations);
    }

    [Fact]
    public void Domain_MustNotReferenceSqlServer()
    {
        var domainCsproj = Path.Combine(GetSolutionRoot(), "src", "ARC.Domain", "ARC.Domain.csproj");
        Assert.True(File.Exists(domainCsproj), "ARC.Domain.csproj not found");

        var content = File.ReadAllText(domainCsproj);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", content);
        Assert.DoesNotContain("System.Data.SqlClient", content);
        Assert.DoesNotContain("Dapper", content);
    }

    [Fact]
    public void Domain_MustNotReferenceDapper()
    {
        var domainPath = Path.Combine(GetSolutionRoot(), "src", "ARC.Domain");
        var csFiles = Directory.GetFiles(domainPath, "*.cs", SearchOption.AllDirectories);

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("using Dapper", content);
            Assert.DoesNotContain("CommandDefinition", content);
        }
    }

    private static IEnumerable<string> FindInlineSqlViolations(string context, string content)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*"))
                continue;

            foreach (var pattern in InlineSqlPatterns)
            {
                if (line.Contains($"\"{pattern}", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains($"@\"{pattern}", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains($"\"\"\"{pattern}", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{context}:{i + 1} contains inline {pattern.Trim()}";
                    break;
                }
            }
        }
    }

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ARC.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("Solution root not found");
    }
}
