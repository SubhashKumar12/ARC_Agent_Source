using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ARC.Domain.Limitation;
using ARC.Domain.Rules;
using ARC.Data.Sql;
using ARC.Tools.DealerMaster;
using ARC.Tools.Drafting;
using ARC.Tools.Evidence;
using ARC.Tools.Field;
using ARC.Tools.Identity;
using ARC.Tools.Insights;
using ARC.Tools.Knowledge;
using ARC.Tools.Legal;
using ARC.Tools.Models;
using ARC.Tools.Notice;
using ARC.Tools.Outstanding;
using ARC.Tools.Persistence;
using ARC.Tools.Reconciliation;
using ARC.Tools.Risk;
using ARC.Tools.Speech;

namespace ARC.Tools.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArcTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcToolsOptions>(configuration.GetSection(ArcToolsOptions.SectionName));

        var rules = RuleConfiguration.SourceIllustrative();
        services.AddSingleton(rules);
        services.AddSingleton(_ => RuleEngine.CreateDefault(rules));
        services.AddSingleton<ILimitationClockService, LimitationClockService>();

        services.AddSingleton<ResolveDealerIdentityTool>();
        services.AddSingleton<GetDealerDetailsTool>();
        services.AddSingleton<GetOutstandingDetailsTool>();
        services.AddSingleton<GetFieldRecoveryDetailsTool>();
        services.AddSingleton<GetLegalRecoveryDetailsTool>();
        services.AddSingleton<GetEvidenceStatusTool>();
        services.AddSingleton<GetNetExposureDetailsTool>();
        services.AddSingleton<ReconciliationTool>();
        services.AddSingleton<RiskPrioritisationTool>();
        services.AddSingleton<NoticeDecisionTool>();
        services.AddSingleton<LegalEligibilityTool>();
        services.AddSingleton<DraftingVerificationTool>();
        services.AddSingleton<FieldOrchestrationTool>();
        services.AddSingleton<EvidenceCaseFileTool>();
        services.AddSingleton<KnowledgeRetrievalTool>();
        services.AddSingleton<SupervisoryInsightTool>();

        services.AddSingleton<IPtpTranscriptParser, DeterministicPtpTranscriptParser>();
        RegisterFieldPersistence(services, configuration);
        services.AddSingleton<VisitPlanAssembler>();
        services.AddSingleton<PtpChaseScanner>();
        services.AddSingleton<PtpCaptureOrchestrator>();
        if (HasSpeechEndpoint(configuration))
            services.AddSingleton<ISpeechTranscriptionService, AzureSpeechTranscriptionService>();
        else
            services.AddSingleton<ISpeechTranscriptionService, DemoSpeechTranscriptionService>();

        return services;
    }

    private static void RegisterFieldPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var sqlConfigured = HasArcSqlConnection(configuration);
        var useInMemory = configuration.GetValue<bool>($"{ArcToolsOptions.SectionName}:FieldPersistence:UseInMemory");

        services.AddSingleton<IFieldPersistenceAvailability>(_ =>
            new FieldPersistenceAvailability(sqlConfigured, useInMemory));

        if (sqlConfigured)
        {
            // ARC-owned SQL SoR (ArcData:Sql) — separate from corporate ODOS AppSettings reads.
            services.AddSingleton<IPtpRepository>(sp =>
                new SqlBackedPtpRepository(sp.GetRequiredService<IPtpRecordRepository>()));
            services.AddSingleton<IPtpCandidateStore>(sp =>
                new PtpRepositoryCandidateStore(sp.GetRequiredService<IPtpRepository>()));
            services.AddSingleton<IVisitPlanStore>(sp =>
                new SqlBackedVisitPlanStore(sp.GetRequiredService<IVisitPlanRepository>()));
            services.AddSingleton<IPtpChaseStore>(sp =>
                new SqlBackedPtpChaseStore(sp.GetRequiredService<IPtpChaseRepository>()));
            return;
        }

        if (useInMemory)
        {
            services.AddSingleton<InMemoryPtpRepository>();
            services.AddSingleton<IPtpRepository>(sp => sp.GetRequiredService<InMemoryPtpRepository>());
            services.AddSingleton<IPtpCandidateStore>(sp =>
                new PtpRepositoryCandidateStore(sp.GetRequiredService<IPtpRepository>()));
            services.AddSingleton<IVisitPlanStore, InMemoryVisitPlanStore>();
            services.AddSingleton<IPtpChaseStore>(sp =>
                new InMemoryPtpChaseStore(sp.GetRequiredService<InMemoryPtpRepository>()));
            return;
        }

        services.AddSingleton<IPtpRepository, NotConfiguredPtpRepository>();
        services.AddSingleton<IPtpCandidateStore>(sp =>
            new PtpRepositoryCandidateStore(sp.GetRequiredService<IPtpRepository>()));
        services.AddSingleton<IVisitPlanStore, NotConfiguredVisitPlanStore>();
        services.AddSingleton<IPtpChaseStore, NotConfiguredPtpChaseStore>();
    }

    private static bool HasSpeechEndpoint(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration[$"{ArcToolsOptions.SectionName}:SpeechEndpoint"]);

    private static bool HasArcSqlConnection(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration["ArcData:Sql:ConnectionString"]);
}
