using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Tools.Models;

namespace ARC.Tools.Speech;

/// <summary>
/// Local/demo Speech. Never calls Azure. Never requires a microphone.
/// Seed transcript/confidence via the request for deterministic S7-style tests.
/// </summary>
public sealed class DemoSpeechTranscriptionService : ISpeechTranscriptionService
{
    private readonly ArcToolsOptions _options;
    private readonly ILogger<DemoSpeechTranscriptionService> _logger;

    public DemoSpeechTranscriptionService(IOptions<ArcToolsOptions> options, ILogger<DemoSpeechTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<SpeechTranscriptionResult> TranscribeAsync(SpeechTranscriptionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsSpeechLocaleAllowed(request.Locale))
        {
            _logger.LogInformation(
                "Demo speech unsupported locale {Locale} correlation {CorrelationId}",
                request.Locale,
                request.CorrelationId);
            return Task.FromResult(new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.UnsupportedLocale,
                Locale = request.Locale,
                Error = "Unsupported speech locale. Capture as text or re-record with an allowed locale."
            });
        }

        if (string.IsNullOrWhiteSpace(request.DemoTranscript))
        {
            _logger.LogInformation(
                "Demo speech no-match correlation {CorrelationId} locale {Locale}",
                request.CorrelationId,
                request.Locale);
            return Task.FromResult(new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.NoMatch,
                Locale = request.Locale,
                Error = "No speech recognized."
            });
        }

        _logger.LogInformation(
            "Demo speech succeeded correlation {CorrelationId} locale {Locale} category {Category}",
            request.CorrelationId,
            request.Locale,
            _options.ConfidenceCategory(request.DemoConfidence));

        return Task.FromResult(new SpeechTranscriptionResult
        {
            Status = SpeechRecognitionStatus.Succeeded,
            Transcript = request.DemoTranscript.Trim(),
            Confidence = request.DemoConfidence,
            Locale = request.Locale.Trim()
        });
    }
}
