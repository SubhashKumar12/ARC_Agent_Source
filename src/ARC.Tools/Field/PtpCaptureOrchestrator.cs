using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using ARC.Tools.Models;
using ARC.Tools.Speech;

namespace ARC.Tools.Field;

/// <summary>
/// Out-of-band Speech → candidate PTP → TSI confirm. Not a RequestPort and not on the ODOS graph.
/// Azure Speech and parsers produce candidates only; they never set ConfirmedByTsi.
/// </summary>
public sealed class PtpCaptureOrchestrator
{
    public const string Name = "PtpCapture";

    private readonly ISpeechTranscriptionService _speech;
    private readonly IPtpTranscriptParser _parser;
    private readonly FieldOrchestrationTool _tool;
    private readonly IPtpCandidateStore _store;
    private readonly IDealerRepository _dealers;
    private readonly ArcToolsOptions _options;
    private readonly ILogger<PtpCaptureOrchestrator> _logger;

    public PtpCaptureOrchestrator(
        ISpeechTranscriptionService speech,
        IPtpTranscriptParser parser,
        FieldOrchestrationTool tool,
        IPtpCandidateStore store,
        IDealerRepository dealers,
        IOptions<ArcToolsOptions> options,
        ILogger<PtpCaptureOrchestrator> logger)
    {
        _speech = speech;
        _parser = parser;
        _tool = tool;
        _store = store;
        _dealers = dealers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PtpSpeechCaptureResult> CaptureAsync(
        PtpSpeechCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");
        if (string.IsNullOrWhiteSpace(request.CycleId))
            throw new ToolException(Name, "CycleId is required.");

        SpeechTranscriptionResult? speech = null;
        if (request.AudioBytes is { Length: > 0 } || request.DemoTranscript is not null)
        {
            speech = await _speech.TranscribeAsync(
                new SpeechTranscriptionRequest(
                    request.Locale,
                    request.CorrelationId,
                    request.AudioBytes,
                    request.DemoTranscript,
                    request.DemoConfidence),
                cancellationToken);

            if (speech.Status is SpeechRecognitionStatus.UnsupportedLocale
                or SpeechRecognitionStatus.NoMatch
                or SpeechRecognitionStatus.Canceled
                or SpeechRecognitionStatus.Timeout
                or SpeechRecognitionStatus.Failed)
            {
                var failed = await PersistAsync(
                    NewIncompleteId(request),
                    request,
                    speech.Status,
                    transcript: null,
                    date: null,
                    amount: null,
                    confidence: speech.Confidence,
                    discarded: false,
                    status: PtpCandidateStatus.Failed,
                    cancellationToken);
                return new PtpSpeechCaptureResult { Candidate = failed, Structured = null };
            }
        }

        var locale = speech?.Locale ?? request.Locale;
        if (!_options.IsSpeechLocaleAllowed(locale) && string.IsNullOrWhiteSpace(request.DemoTranscript)
            && request.StructuredCommitmentDate is null)
        {
            var unsupported = await PersistAsync(
                NewIncompleteId(request),
                request,
                SpeechRecognitionStatus.UnsupportedLocale,
                transcript: null,
                date: null,
                amount: null,
                confidence: null,
                discarded: false,
                status: PtpCandidateStatus.Failed,
                cancellationToken);
            return new PtpSpeechCaptureResult { Candidate = unsupported, Structured = null };
        }

        var confidence = speech?.Confidence ?? request.DemoConfidence;
        var transcript = speech?.Transcript;
        var parsed = _parser.Parse(transcript, request.AsOf);
        var date = request.StructuredCommitmentDate ?? parsed.CommitmentDate;
        var amount = request.StructuredAmount ?? parsed.Amount;

        if (date is null || amount is not > 0m)
        {
            var incomplete = await PersistAsync(
                NewIncompleteId(request),
                request,
                speech?.Status,
                transcript,
                date,
                amount,
                confidence,
                discarded: false,
                status: PtpCandidateStatus.Incomplete,
                cancellationToken);
            _logger.LogInformation(
                "PTP candidate incomplete dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} locale {Locale} category {Category}",
                request.DealerUrn, request.CycleId, request.CorrelationId, locale, _options.ConfidenceCategory(confidence));
            return new PtpSpeechCaptureResult { Candidate = incomplete, Structured = null };
        }

        var structured = _tool.CapturePromiseToPay(new CapturePromiseToPayRequest(
            request.DealerUrn,
            date.Value,
            amount.Value,
            ConfirmedByTsi: false,
            confidence,
            request.AsOf,
            request.CycleId,
            request.CorrelationId));

        var status = structured.DiscardedLowConfidence ? PtpCandidateStatus.Discarded : PtpCandidateStatus.Captured;
        var candidate = await PersistAsync(
            structured.RecordId,
            request,
            speech?.Status ?? SpeechRecognitionStatus.Succeeded,
            transcript,
            date,
            amount,
            confidence,
            structured.DiscardedLowConfidence,
            status,
            cancellationToken);

        _logger.LogInformation(
            "PTP candidate {Status} record {RecordId} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} locale {Locale} category {Category} requiresTsi {RequiresTsi}",
            status,
            candidate.RecordId,
            request.DealerUrn,
            request.CycleId,
            request.CorrelationId,
            locale,
            _options.ConfidenceCategory(confidence),
            true);

        return new PtpSpeechCaptureResult { Candidate = candidate, Structured = structured };
    }

    public async Task<PtpConfirmResult> ConfirmAsync(PtpConfirmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Actor.Role != ActorRole.Tsi)
            throw new ToolException(Name, "Only a TSI actor may confirm a Promise-to-Pay candidate.");

        var candidate = await _store.GetAsync(request.RecordId, cancellationToken)
            ?? throw new ToolException(Name, $"PTP candidate '{request.RecordId}' was not found.");

        if (candidate.Status == PtpCandidateStatus.Confirmed && candidate.Committed is not null)
            throw new ToolException(Name, "PTP candidate is already confirmed.");
        if (candidate.Discarded || candidate.Status == PtpCandidateStatus.Discarded)
            throw new ToolException(Name, "A discarded PTP candidate cannot be confirmed.");
        if (candidate.Status == PtpCandidateStatus.Failed)
            throw new ToolException(Name, "A failed speech capture cannot be confirmed.");

        var dealer = await _dealers.GetAsync(new DealerUrn(candidate.DealerUrn), cancellationToken)
            ?? throw new ToolException(Name, $"Dealer '{candidate.DealerUrn}' was not found.");

        if (!ActorCanConfirm(request.Actor, dealer))
            throw new ToolException(Name, "Dealer is outside the actor's region or depot.");

        var date = request.CommitmentDate ?? candidate.CommitmentDate;
        var amount = request.Amount ?? candidate.Amount;
        if (date is null || amount is not > 0m)
            throw new ToolException(Name, "TSI must supply a commitment date and amount greater than zero before confirm.");

        var committed = new PromiseToPay(
            new DealerUrn(candidate.DealerUrn),
            date.Value,
            new Money(amount.Value),
            confirmedByTsi: true);

        var confirmed = new PtpCandidateRecord
        {
            RecordId = candidate.RecordId,
            DealerUrn = candidate.DealerUrn,
            CycleId = candidate.CycleId,
            CommitmentDate = date,
            Amount = amount,
            SpeechConfidence = candidate.SpeechConfidence,
            Locale = candidate.Locale,
            RequiresTsiConfirmation = false,
            Discarded = false,
            CorrelationId = request.CorrelationId ?? candidate.CorrelationId,
            Status = PtpCandidateStatus.Confirmed,
            TranscriptSha256 = candidate.TranscriptSha256,
            RecognitionStatus = candidate.RecognitionStatus,
            Committed = committed,
            ConfirmedUtc = DateTimeOffset.UtcNow,
            ConfirmedByUpn = request.Actor.Upn
        };
        await _store.SaveAsync(confirmed, cancellationToken);

        _logger.LogInformation(
            "PTP confirmed record {RecordId} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} actor {Actor}",
            confirmed.RecordId,
            confirmed.DealerUrn,
            confirmed.CycleId,
            confirmed.CorrelationId,
            request.Actor.Upn);

        return new PtpConfirmResult(confirmed, committed);
    }

    private async Task<PtpCandidateRecord> PersistAsync(
        string recordId,
        PtpSpeechCaptureRequest request,
        SpeechRecognitionStatus? recognition,
        string? transcript,
        DateOnly? date,
        decimal? amount,
        decimal? confidence,
        bool discarded,
        PtpCandidateStatus status,
        CancellationToken cancellationToken)
    {
        var record = new PtpCandidateRecord
        {
            RecordId = recordId,
            DealerUrn = request.DealerUrn,
            CycleId = request.CycleId,
            CommitmentDate = date,
            Amount = amount,
            SpeechConfidence = confidence,
            Locale = request.Locale,
            RequiresTsiConfirmation = status is PtpCandidateStatus.Captured or PtpCandidateStatus.Incomplete,
            Discarded = discarded,
            CorrelationId = request.CorrelationId,
            Status = status,
            TranscriptSha256 = HashTranscript(transcript),
            RecognitionStatus = recognition,
            Committed = null
        };
        await _store.SaveAsync(record, cancellationToken);
        return record;
    }

    private static bool ActorCanConfirm(FieldActor actor, Dealer dealer)
    {
        if (actor.Role != ActorRole.Tsi)
            return false;
        if (string.IsNullOrWhiteSpace(actor.Region)
            || !string.Equals(actor.Region, dealer.Region, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(actor.Depot)
            && !string.Equals(actor.Depot, dealer.Depot, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string NewIncompleteId(PtpSpeechCaptureRequest request)
        => $"{request.CycleId}|{request.DealerUrn}|ptp-candidate|{Guid.NewGuid():N}";

    private static string? HashTranscript(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(transcript));
        return Convert.ToHexString(hash);
    }
}
