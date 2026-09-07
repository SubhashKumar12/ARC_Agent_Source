using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Tools.Models;

namespace ARC.Tools.Speech;

/// <summary>
/// One-shot Azure Speech recognition. SDK types stay in this adapter — Domain never sees them.
/// Authentication: <see cref="SpeechConfig.FromEndpoint(Uri, TokenCredential)"/> with
/// <see cref="DefaultAzureCredential"/> (Managed Identity / Entra). Requires a Speech
/// resource custom-domain endpoint. Subscription keys are not used.
/// </summary>
public sealed class AzureSpeechTranscriptionService : ISpeechTranscriptionService
{
    private readonly ArcToolsOptions _options;
    private readonly ILogger<AzureSpeechTranscriptionService> _logger;
    private readonly ISpeechSdkRecognizer _recognizer;

    public AzureSpeechTranscriptionService(
        IOptions<ArcToolsOptions> options,
        ILogger<AzureSpeechTranscriptionService> logger)
        : this(options, logger, new DefaultSpeechSdkRecognizer(options.Value))
    {
    }

    public AzureSpeechTranscriptionService(
        IOptions<ArcToolsOptions> options,
        ILogger<AzureSpeechTranscriptionService> logger,
        ISpeechSdkRecognizer recognizer)
    {
        _options = options.Value;
        _logger = logger;
        _recognizer = recognizer;
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(
        SpeechTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsSpeechLocaleAllowed(request.Locale))
        {
            _logger.LogInformation(
                "Speech unsupported locale {Locale} correlation {CorrelationId}",
                request.Locale,
                request.CorrelationId);
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.UnsupportedLocale,
                Locale = request.Locale,
                Error = "Unsupported speech locale. Capture as text or re-record with an allowed locale."
            };
        }

        if (request.AudioBytes is not { Length: > 0 })
        {
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.NoMatch,
                Locale = request.Locale,
                Error = "Audio is required for Azure Speech."
            };
        }

        var seconds = _options.SpeechTimeoutSeconds <= 0 ? 30 : _options.SpeechTimeoutSeconds;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

        try
        {
            _logger.LogInformation(
                "Speech recognize start correlation {CorrelationId} locale {Locale}",
                request.CorrelationId,
                request.Locale);

            var raw = await _recognizer.RecognizeOnceAsync(
                request.AudioBytes,
                request.Locale.Trim(),
                timeout.Token);

            return Map(raw, request);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Speech timeout or cancel correlation {CorrelationId}", request.CorrelationId);
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.Timeout,
                Locale = request.Locale,
                Error = "Speech recognition timed out."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech failed correlation {CorrelationId}", request.CorrelationId);
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.Failed,
                Locale = request.Locale,
                Error = "Speech recognition failed."
            };
        }
    }

    private SpeechTranscriptionResult Map(SpeechSdkRecognition raw, SpeechTranscriptionRequest request)
    {
        if (raw.Status == SpeechRecognitionStatus.Canceled)
        {
            _logger.LogWarning(
                "Speech canceled correlation {CorrelationId} reason {Reason}",
                request.CorrelationId,
                raw.CancelReason ?? "unknown");
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.Canceled,
                Locale = request.Locale,
                Error = "Speech recognition was canceled."
            };
        }

        if (raw.Status == SpeechRecognitionStatus.NoMatch)
        {
            _logger.LogInformation("Speech no-match correlation {CorrelationId}", request.CorrelationId);
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.NoMatch,
                Locale = request.Locale,
                Error = "No speech recognized."
            };
        }

        if (raw.Status != SpeechRecognitionStatus.Succeeded)
        {
            return new SpeechTranscriptionResult
            {
                Status = SpeechRecognitionStatus.Failed,
                Locale = request.Locale,
                Error = "Speech recognition failed."
            };
        }

        _logger.LogInformation(
            "Speech succeeded correlation {CorrelationId} locale {Locale} category {Category}",
            request.CorrelationId,
            request.Locale,
            _options.ConfidenceCategory(raw.Confidence));

        return new SpeechTranscriptionResult
        {
            Status = SpeechRecognitionStatus.Succeeded,
            Transcript = string.IsNullOrWhiteSpace(raw.Text) ? null : raw.Text.Trim(),
            Confidence = raw.Confidence,
            Locale = request.Locale.Trim()
        };
    }
}

/// <summary>Test seam. Production uses the Azure Speech SDK; tests inject fakes. Not a Domain type.</summary>
public interface ISpeechSdkRecognizer
{
    Task<SpeechSdkRecognition> RecognizeOnceAsync(byte[] audioBytes, string locale, CancellationToken cancellationToken);
}

public sealed record SpeechSdkRecognition(
    SpeechRecognitionStatus Status,
    string? Text,
    decimal? Confidence,
    string? CancelReason);

internal sealed class DefaultSpeechSdkRecognizer : ISpeechSdkRecognizer
{
    private readonly ArcToolsOptions _options;
    private readonly object _gate = new();
    private SpeechConfig? _config;

    public DefaultSpeechSdkRecognizer(ArcToolsOptions options) => _options = options;

    public async Task<SpeechSdkRecognition> RecognizeOnceAsync(
        byte[] audioBytes,
        string locale,
        CancellationToken cancellationToken)
    {
        var config = Config;
        config.SpeechRecognitionLanguage = locale;

        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        using var push = AudioInputStream.CreatePushStream(format);
        push.Write(audioBytes);
        push.Close();
        using var audio = AudioConfig.FromStreamInput(push);
        using var recognizer = new SpeechRecognizer(config, audio);

        var result = await recognizer.RecognizeOnceAsync().WaitAsync(cancellationToken);
        return MapSdk(result);
    }

    private SpeechConfig Config
    {
        get
        {
            if (_config is not null)
                return _config;
            lock (_gate)
            {
                if (_config is not null)
                    return _config;
                if (string.IsNullOrWhiteSpace(_options.SpeechEndpoint))
                    throw new InvalidOperationException("ArcTools:SpeechEndpoint is not configured.");
                if (!_options.SpeechUseManagedIdentity)
                    throw new InvalidOperationException("Azure Speech requires Managed Identity. API keys are not supported.");

                TokenCredential credential = new DefaultAzureCredential();
                _config = SpeechConfig.FromEndpoint(new Uri(_options.SpeechEndpoint), credential);
                return _config;
            }
        }
    }

    private static SpeechSdkRecognition MapSdk(SpeechRecognitionResult result)
    {
        if (result.Reason == ResultReason.Canceled)
        {
            var cancel = CancellationDetails.FromResult(result);
            return new SpeechSdkRecognition(SpeechRecognitionStatus.Canceled, null, null, cancel.Reason.ToString());
        }

        if (result.Reason == ResultReason.NoMatch)
            return new SpeechSdkRecognition(SpeechRecognitionStatus.NoMatch, null, null, null);

        if (result.Reason != ResultReason.RecognizedSpeech)
            return new SpeechSdkRecognition(SpeechRecognitionStatus.Failed, null, null, result.Reason.ToString());

        decimal? confidence = null;
        var best = result.Best()?.FirstOrDefault();
        if (best is not null)
            confidence = (decimal)best.Confidence;

        return new SpeechSdkRecognition(
            SpeechRecognitionStatus.Succeeded,
            string.IsNullOrWhiteSpace(result.Text) ? null : result.Text.Trim(),
            confidence,
            null);
    }
}
