using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Agents.A6FieldOrchestration;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Tests.Support;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Tools.DependencyInjection;
using ARC.Tools.Exceptions;
using ARC.Tools.Field;
using ARC.Tools.Models;
using ARC.Tools.Notice;
using ARC.Tools.Persistence;
using ARC.Tools.Speech;

namespace ARC.Agents.Tests;

public sealed class SpeechRegistrationTests
{
    [Fact]
    public void AddArcTools_registers_azure_speech_when_endpoint_configured()
    {
        using var provider = BuildTools(("ArcTools:SpeechEndpoint", "https://arc-speech.cognitiveservices.azure.com/"));
        Assert.IsType<AzureSpeechTranscriptionService>(provider.GetRequiredService<ISpeechTranscriptionService>());
    }

    [Fact]
    public void AddArcTools_registers_demo_speech_without_azure()
    {
        using var provider = BuildTools();
        Assert.IsType<DemoSpeechTranscriptionService>(provider.GetRequiredService<ISpeechTranscriptionService>());
        Assert.IsType<PtpRepositoryCandidateStore>(provider.GetRequiredService<IPtpCandidateStore>());
        Assert.Same(
            provider.GetRequiredService<InMemoryPtpRepository>(),
            provider.GetRequiredService<IPtpRepository>());
        Assert.NotNull(provider.GetRequiredService<PtpCaptureOrchestrator>());
    }

    [Fact]
    public void Demo_speech_can_replace_azure_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDealerRepository, MissingDealers>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcTools:SpeechEndpoint"] = "https://arc-speech.cognitiveservices.azure.com/"
            })
            .Build();
        services.AddArcTools(configuration);
        services.AddSingleton<ISpeechTranscriptionService, DemoSpeechTranscriptionService>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DemoSpeechTranscriptionService>(provider.GetRequiredService<ISpeechTranscriptionService>());
    }

    private static ServiceProvider BuildTools(params (string Key, string? Value)[] values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDealerRepository, MissingDealers>();
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        map["ArcTools:FieldPersistence:UseInMemory"] = "true";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(map)
            .Build();
        services.AddArcTools(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class MissingDealers : IDealerRepository
    {
        public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken) => Task.FromResult<Dealer?>(null);
        public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Dealer>>([]);
        public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Dealer>>([]);
    }
}

public sealed class DemoSpeechTranscriptionTests
{
    [Fact]
    public async Task Demo_transcription_returns_seeded_transcript_and_confidence()
    {
        var logger = new CollectingLogger<DemoSpeechTranscriptionService>();
        var speech = new DemoSpeechTranscriptionService(Options.Create(new ArcToolsOptions()), logger);
        var result = await speech.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-speech", DemoTranscript: "I will pay 25000 on Friday", DemoConfidence: 0.91m),
            CancellationToken.None);

        Assert.Equal(SpeechRecognitionStatus.Succeeded, result.Status);
        Assert.Equal("I will pay 25000 on Friday", result.Transcript);
        Assert.Equal(0.91m, result.Confidence);
        Assert.Equal("en-IN", result.Locale);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("25000", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("I will pay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unsupported_locale_fails_closed()
    {
        var speech = new DemoSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechAllowedLocales = ["en-IN"] }),
            NullLogger<DemoSpeechTranscriptionService>.Instance);
        var result = await speech.TranscribeAsync(
            new SpeechTranscriptionRequest("ta-IN", "corr-locale", DemoTranscript: "நான் செலுத்துவேன்", DemoConfidence: 0.9m),
            CancellationToken.None);

        Assert.Equal(SpeechRecognitionStatus.UnsupportedLocale, result.Status);
        Assert.Null(result.Transcript);
        Assert.Contains("text or re-record", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Demo_no_match_fails_safely()
    {
        var speech = new DemoSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions()),
            NullLogger<DemoSpeechTranscriptionService>.Instance);
        var result = await speech.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-nomatch"),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.NoMatch, result.Status);
        Assert.Null(result.Transcript);
    }
}

public sealed class AzureSpeechAdapterTests
{
    [Fact]
    public async Task Azure_unsupported_locale_does_not_call_sdk()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => throw new InvalidOperationException("SDK must not run"));
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("ta-IN", "corr-az-locale", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.UnsupportedLocale, result.Status);
        Assert.False(sdk.Called);
    }

    [Fact]
    public async Task Azure_success_returns_transcript_and_confidence()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => new SpeechSdkRecognition(
            SpeechRecognitionStatus.Succeeded, "I will pay 25000 on Friday", 0.91m, null));
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-az-ok", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.Succeeded, result.Status);
        Assert.Equal("I will pay 25000 on Friday", result.Transcript);
        Assert.Equal(0.91m, result.Confidence);
    }

    [Fact]
    public async Task Azure_no_match_fails_safely()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => new SpeechSdkRecognition(
            SpeechRecognitionStatus.NoMatch, null, null, null));
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-az-nomatch", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.NoMatch, result.Status);
        Assert.Null(result.Transcript);
    }

    [Fact]
    public async Task Azure_cancel_fails_safely()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => new SpeechSdkRecognition(
            SpeechRecognitionStatus.Canceled, null, null, "Error"));
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-az-cancel", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task Azure_timeout_fails_safely()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => throw new OperationCanceledException());
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-az-timeout", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task Azure_sdk_failure_fails_closed()
    {
        var sdk = new ScriptedSpeechSdkRecognizer(_ => throw new InvalidOperationException("speech down"));
        var azure = new AzureSpeechTranscriptionService(
            Options.Create(new ArcToolsOptions { SpeechEndpoint = "https://arc-speech.cognitiveservices.azure.com/" }),
            NullLogger<AzureSpeechTranscriptionService>.Instance,
            sdk);
        var result = await azure.TranscribeAsync(
            new SpeechTranscriptionRequest("en-IN", "corr-az-fail", AudioBytes: [1, 2, 3]),
            CancellationToken.None);
        Assert.Equal(SpeechRecognitionStatus.Failed, result.Status);
        Assert.Null(result.Transcript);
    }
}

public sealed class PtpTranscriptParserTests
{
    private readonly DeterministicPtpTranscriptParser _parser = new();
    private static readonly DateOnly AsOf = new(2026, 3, 1);

    [Fact]
    public void Transcript_parse_successful_amount_and_friday()
    {
        var parsed = _parser.Parse("I will pay 25000 on Friday", AsOf);
        Assert.Equal(25_000m, parsed.Amount);
        Assert.Equal(new DateOnly(2026, 3, 6), parsed.CommitmentDate);
        Assert.True(parsed.IsComplete);
    }

    [Fact]
    public void Transcript_with_no_amount_returns_null_incomplete()
    {
        var parsed = _parser.Parse("I will pay on 2026-04-15", AsOf);
        Assert.Null(parsed.Amount);
        Assert.Equal(new DateOnly(2026, 4, 15), parsed.CommitmentDate);
        Assert.False(parsed.IsComplete);
    }

    [Fact]
    public void Transcript_with_no_date_returns_null_incomplete()
    {
        var parsed = _parser.Parse("I will pay 25000 soon", AsOf);
        Assert.Equal(25_000m, parsed.Amount);
        Assert.Null(parsed.CommitmentDate);
        Assert.False(parsed.IsComplete);
    }

    [Fact]
    public void Ambiguous_soon_does_not_invent_values()
    {
        var parsed = _parser.Parse("I will pay soon", AsOf);
        Assert.Null(parsed.Amount);
        Assert.Null(parsed.CommitmentDate);
        Assert.False(parsed.IsComplete);
    }
}

public sealed class PtpCaptureOrchestratorTests
{
    [Fact]
    public async Task S7_0_72_requires_confirmation_and_is_not_auto_committed()
    {
        var (orchestrator, _, _) = Create();
        var result = await CaptureS7(orchestrator, 0.72m);

        Assert.Equal(PtpCandidateStatus.Captured, result.Candidate.Status);
        Assert.True(result.Candidate.RequiresTsiConfirmation);
        Assert.False(result.Candidate.Discarded);
        Assert.False(result.Structured!.Promise.ConfirmedByTsi);
        Assert.Equal(0.72m, result.Candidate.SpeechConfidence);
    }

    [Fact]
    public async Task High_confidence_still_does_not_auto_confirm()
    {
        var (orchestrator, _, _) = Create();
        var result = await CaptureS7(orchestrator, 0.99m);

        Assert.Equal(PtpCandidateStatus.Captured, result.Candidate.Status);
        Assert.True(result.Candidate.RequiresTsiConfirmation);
        Assert.False(result.Structured!.Promise.ConfirmedByTsi);
        Assert.Null(result.Candidate.Committed);
    }

    [Fact]
    public async Task Configured_discard_floor_blocks_low_confidence()
    {
        var (orchestrator, store, _) = Create(o => o.VoicePtpDiscardBelow = 0.50m);
        var result = await CaptureS7(orchestrator, 0.40m);

        Assert.True(result.Candidate.Discarded);
        Assert.Equal(PtpCandidateStatus.Discarded, result.Candidate.Status);
        var loaded = await store.GetAsync(result.Candidate.RecordId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded!.Discarded);
    }

    [Fact]
    public async Task Null_discard_floor_does_not_invent_discard_behavior()
    {
        var (orchestrator, _, _) = Create(o => o.VoicePtpDiscardBelow = null);
        var result = await CaptureS7(orchestrator, 0.40m);

        Assert.False(result.Candidate.Discarded);
        Assert.Equal(PtpCandidateStatus.Captured, result.Candidate.Status);
        Assert.True(result.Candidate.RequiresTsiConfirmation);
        Assert.False(result.Structured!.Promise.ConfirmedByTsi);
    }

    [Fact]
    public async Task Candidate_is_persisted()
    {
        var (orchestrator, store, _) = Create();
        var result = await CaptureS7(orchestrator, 0.72m);
        var loaded = await store.GetAsync(result.Candidate.RecordId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("dealer:ptp", loaded!.DealerUrn);
        Assert.Equal("2026-03-s7", loaded.CycleId);
        Assert.Equal(new DateOnly(2026, 4, 15), loaded.CommitmentDate);
        Assert.Equal(25_000m, loaded.Amount);
        Assert.Equal("en-IN", loaded.Locale);
        Assert.Equal("corr-ptp", loaded.CorrelationId);
        Assert.Null(loaded.Committed);
    }

    [Fact]
    public async Task Transcript_parse_creates_candidate_without_structured_fields()
    {
        var (orchestrator, _, _) = Create();
        var result = await orchestrator.CaptureAsync(
            new PtpSpeechCaptureRequest(
                "dealer:ptp",
                "2026-03-parse",
                new DateOnly(2026, 3, 1),
                "en-IN",
                "corr-parse",
                DemoTranscript: "I will pay 25000 on Friday",
                DemoConfidence: 0.72m),
            CancellationToken.None);

        Assert.Equal(PtpCandidateStatus.Captured, result.Candidate.Status);
        Assert.Equal(25_000m, result.Candidate.Amount);
        Assert.Equal(new DateOnly(2026, 3, 6), result.Candidate.CommitmentDate);
        Assert.False(result.Structured!.Promise.ConfirmedByTsi);
        Assert.NotNull(result.Candidate.TranscriptSha256);
    }

    [Fact]
    public async Task Incomplete_transcript_is_not_committable()
    {
        var (orchestrator, _, _) = Create();
        var result = await orchestrator.CaptureAsync(
            new PtpSpeechCaptureRequest(
                "dealer:ptp",
                "2026-03-soon",
                new DateOnly(2026, 3, 1),
                "en-IN",
                "corr-soon",
                DemoTranscript: "I will pay soon",
                DemoConfidence: 0.9m),
            CancellationToken.None);

        Assert.Equal(PtpCandidateStatus.Incomplete, result.Candidate.Status);
        Assert.Null(result.Structured);
        Assert.Null(result.Candidate.Amount);
        Assert.Null(result.Candidate.CommitmentDate);
    }

    [Fact]
    public async Task Tsi_confirmation_commits_ptp()
    {
        var (orchestrator, store, _) = Create();
        var captured = await CaptureS7(orchestrator, 0.72m);
        var confirmed = await orchestrator.ConfirmAsync(
            new PtpConfirmRequest(captured.Candidate.RecordId, TsiWest(), new DateOnly(2026, 3, 1), CorrelationId: "corr-confirm"),
            CancellationToken.None);

        Assert.True(confirmed.Committed.ConfirmedByTsi);
        Assert.Equal(PtpCandidateStatus.Confirmed, confirmed.Candidate.Status);
        var loaded = await store.GetAsync(captured.Candidate.RecordId, CancellationToken.None);
        Assert.True(loaded!.Committed!.ConfirmedByTsi);
    }

    [Fact]
    public async Task Non_tsi_actor_cannot_confirm()
    {
        var (orchestrator, _, _) = Create();
        var captured = await CaptureS7(orchestrator, 0.72m);
        var ex = await Assert.ThrowsAsync<ToolException>(() => orchestrator.ConfirmAsync(
            new PtpConfirmRequest(
                captured.Candidate.RecordId,
                new FieldActor("depot.manager@paintco.local", ActorRole.DepotManager, "West", "Mumbai-Andheri"),
                new DateOnly(2026, 3, 1)),
            CancellationToken.None));
        Assert.Contains("TSI", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_region_cannot_confirm()
    {
        var (orchestrator, _, _) = Create();
        var captured = await CaptureS7(orchestrator, 0.72m);
        var ex = await Assert.ThrowsAsync<ToolException>(() => orchestrator.ConfirmAsync(
            new PtpConfirmRequest(
                captured.Candidate.RecordId,
                new FieldActor("tsi.east@paintco.local", ActorRole.Tsi, "East", null),
                new DateOnly(2026, 3, 1)),
            CancellationToken.None));
        Assert.Contains("region or depot", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_depot_cannot_confirm()
    {
        var (orchestrator, _, _) = Create();
        var captured = await CaptureS7(orchestrator, 0.72m);
        var ex = await Assert.ThrowsAsync<ToolException>(() => orchestrator.ConfirmAsync(
            new PtpConfirmRequest(
                captured.Candidate.RecordId,
                new FieldActor("tsi.west@paintco.local", ActorRole.Tsi, "West", "Pune"),
                new DateOnly(2026, 3, 1)),
            CancellationToken.None));
        Assert.Contains("region or depot", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discarded_candidate_cannot_confirm()
    {
        var (orchestrator, _, _) = Create(o => o.VoicePtpDiscardBelow = 0.50m);
        var captured = await CaptureS7(orchestrator, 0.40m);
        var ex = await Assert.ThrowsAsync<ToolException>(() => orchestrator.ConfirmAsync(
            new PtpConfirmRequest(captured.Candidate.RecordId, TsiWest(), new DateOnly(2026, 3, 1)),
            CancellationToken.None));
        Assert.Contains("discarded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Speech_failure_does_not_create_committable_ptp()
    {
        var (orchestrator, _, _) = Create();
        var result = await orchestrator.CaptureAsync(
            new PtpSpeechCaptureRequest(
                "dealer:ptp",
                "2026-03-fail",
                new DateOnly(2026, 3, 1),
                "en-IN",
                "corr-fail",
                DemoTranscript: "   ",
                DemoConfidence: 0.9m),
            CancellationToken.None);

        Assert.Equal(PtpCandidateStatus.Failed, result.Candidate.Status);
        Assert.Null(result.Structured);
    }

    [Fact]
    public async Task Sensitive_transcript_is_not_logged()
    {
        var logger = new CollectingLogger<PtpCaptureOrchestrator>();
        var (orchestrator, _, _) = Create(logger: logger);
        await orchestrator.CaptureAsync(
            new PtpSpeechCaptureRequest(
                "dealer:ptp",
                "2026-03-pii",
                new DateOnly(2026, 3, 1),
                "en-IN",
                "corr-pii",
                DemoTranscript: "I will pay 25000 to account 9876543210 on Friday",
                DemoConfidence: 0.72m),
            CancellationToken.None);

        Assert.DoesNotContain(logger.Messages, m => m.Contains("25000", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("9876543210", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("I will pay", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Messages, m => m.Contains("corr-pii", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A6_transcript_path_parses_without_llm_and_never_confirms()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        store.SeedDealer(new Dealer(new DealerUrn("dealer:a6-voice"), false, "SAP-1", "PORTAL-1", "Mumbai", "West", "tsi@paintco.local"));
        var result = await host.GetRequiredService<FieldOrchestrationAgent>().RunAsync(
            new FieldOrchestrationAgentRequest(
                FieldAgentAction.CapturePromiseToPay,
                "dealer:a6-voice",
                RecoveryTier.Notice,
                null,
                null,
                0.72m,
                null,
                "I will pay 25000 on 2026-04-15",
                new AgentContext(new DateOnly(2026, 3, 1), "2026-03-test", "corr-a6-voice", "dealer:a6-voice")),
            CancellationToken.None);

        Assert.NotNull(result.Promise);
        Assert.Equal(25_000m, result.Promise!.Promise.Amount.Amount);
        Assert.Equal(new DateOnly(2026, 4, 15), result.Promise.Promise.CommitmentDate);
        Assert.True(result.Promise.RequiresTsiConfirmation);
        Assert.False(result.Promise.Promise.ConfirmedByTsi);
    }

    private static async Task<PtpSpeechCaptureResult> CaptureS7(PtpCaptureOrchestrator orchestrator, decimal confidence)
        => await orchestrator.CaptureAsync(
            new PtpSpeechCaptureRequest(
                "dealer:ptp",
                "2026-03-s7",
                new DateOnly(2026, 3, 1),
                "en-IN",
                "corr-ptp",
                DemoConfidence: confidence,
                StructuredCommitmentDate: new DateOnly(2026, 4, 15),
                StructuredAmount: 25_000m),
            CancellationToken.None);

    private static FieldActor TsiWest()
        => new("tsi.west@paintco.local", ActorRole.Tsi, "West", null);

    private static (PtpCaptureOrchestrator Orchestrator, IPtpCandidateStore Store, InMemoryHarness Dealers) Create(
        Action<ArcToolsOptions>? configure = null,
        ILogger<PtpCaptureOrchestrator>? logger = null)
    {
        var dealers = new InMemoryHarness();
        dealers.SeedDealer(new Dealer(
            new DealerUrn("dealer:ptp"),
            false,
            sapCode: "SAP-1",
            portalId: "PORTAL-1",
            depot: "Mumbai-Andheri",
            region: "West",
            coveringTsi: "tsi.west@paintco.local"));

        var options = new ArcToolsOptions
        {
            VoicePtpConfirmBelow = 0.80m,
            SpeechAllowedLocales = ["en-IN"]
        };
        configure?.Invoke(options);
        var ptpRepo = new InMemoryPtpRepository();
        var store = new PtpRepositoryCandidateStore(ptpRepo);
        var speech = new DemoSpeechTranscriptionService(
            Options.Create(options),
            NullLogger<DemoSpeechTranscriptionService>.Instance);
        var tool = new FieldOrchestrationTool(dealers, Options.Create(options), NullLogger<FieldOrchestrationTool>.Instance);
        var orchestrator = new PtpCaptureOrchestrator(
            speech,
            new DeterministicPtpTranscriptParser(),
            tool,
            store,
            dealers,
            Options.Create(options),
            logger ?? NullLogger<PtpCaptureOrchestrator>.Instance);
        return (orchestrator, store, dealers);
    }
}

public sealed class PtpNoticeSafetyTests
{
    [Fact]
    public void Unconfirmed_candidate_does_not_affect_r1c_through_notice_tool()
    {
        var tool = NoticeTool();
        var unconfirmed = new PromiseToPay(
            new DealerUrn("dealer:n"),
            new DateOnly(2026, 8, 15),
            new Money(25_000m),
            confirmedByTsi: false);
        var verdict = tool.Decide(NoticeRequest(unconfirmed));
        Assert.Equal(NoticeDecision.Issue, verdict.Decision);
        Assert.DoesNotContain(verdict.RuleResults, r => r.RuleId == "R1c" && r.Blocks);
    }

    [Fact]
    public void Confirmed_ptp_participates_in_existing_r1c()
    {
        var tool = NoticeTool();
        var confirmed = new PromiseToPay(
            new DealerUrn("dealer:n"),
            new DateOnly(2026, 8, 15),
            new Money(25_000m),
            confirmedByTsi: true);
        var verdict = tool.Decide(NoticeRequest(confirmed));
        Assert.Equal(NoticeDecision.Hold, verdict.Decision);
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R1c" && r.Blocks);
    }

    [Fact]
    public void Broken_ptp_check_remains_asof_after_commitment_date()
    {
        var tool = new FieldOrchestrationTool(
            new InMemoryHarness(),
            Options.Create(new ArcToolsOptions()),
            NullLogger<FieldOrchestrationTool>.Instance);
        var ptp = new PromiseToPay(
            new DealerUrn("dealer:n"),
            new DateOnly(2026, 4, 15),
            new Money(25_000m),
            confirmedByTsi: true);

        var broken = tool.CheckBrokenPromise(new BrokenPromiseCheckRequest(ptp, new DateOnly(2026, 4, 16), "corr-broken"));
        var onDate = tool.CheckBrokenPromise(new BrokenPromiseCheckRequest(ptp, new DateOnly(2026, 4, 15), "corr-broken"));

        Assert.True(broken.IsBroken);
        Assert.False(onDate.IsBroken);
    }

    private static NoticeDecisionTool NoticeTool()
    {
        var config = RuleConfiguration.SourceIllustrative();
        return new NoticeDecisionTool(RuleEngine.CreateDefault(config), config, NullLogger<NoticeDecisionTool>.Instance);
    }

    private static NoticeDecisionRequest NoticeRequest(PromiseToPay ptp)
    {
        var urn = new DealerUrn("dealer:n");
        var asOf = new DateOnly(2026, 8, 1);
        var exposure = MetricContract.Compute(
            urn,
            asOf,
            new Money(100_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 1))],
            true);
        return new NoticeDecisionRequest(
            new Dealer(urn, false, "SAP-1", "PORTAL-1", "Mumbai", "West", "tsi@paintco.local"),
            exposure,
            asOf,
            null,
            ptp,
            null,
            "corr-notice");
    }
}

internal sealed class ScriptedSpeechSdkRecognizer : ISpeechSdkRecognizer
{
    private readonly Func<byte[], SpeechSdkRecognition> _script;
    public bool Called { get; private set; }

    public ScriptedSpeechSdkRecognizer(Func<byte[], SpeechSdkRecognition> script) => _script = script;

    public Task<SpeechSdkRecognition> RecognizeOnceAsync(byte[] audioBytes, string locale, CancellationToken cancellationToken)
    {
        Called = true;
        return Task.FromResult(_script(audioBytes));
    }
}

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
