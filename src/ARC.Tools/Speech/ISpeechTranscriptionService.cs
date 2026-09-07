namespace ARC.Tools.Speech;

public enum SpeechRecognitionStatus
{
    Succeeded = 0,
    NoMatch = 1,
    Canceled = 2,
    Timeout = 3,
    Failed = 4,
    UnsupportedLocale = 5
}

/// <summary>Audio → transcript + ASR confidence. Not a PTP decision.</summary>
public interface ISpeechTranscriptionService
{
    Task<SpeechTranscriptionResult> TranscribeAsync(SpeechTranscriptionRequest request, CancellationToken cancellationToken);
}

public sealed record SpeechTranscriptionRequest(
    string Locale,
    string? CorrelationId,
    byte[]? AudioBytes = null,
    string? DemoTranscript = null,
    decimal? DemoConfidence = null);

public sealed record SpeechTranscriptionResult
{
    public required SpeechRecognitionStatus Status { get; init; }
    public string? Transcript { get; init; }
    public decimal? Confidence { get; init; }
    public required string Locale { get; init; }
    public string? Error { get; init; }
}
