using WhisperMd.App.Backend;

namespace WhisperMd.App.Execution;

public sealed record TranscriptionItemRunResult(
    Guid RecordingId,
    string InputPath,
    int Order,
    bool Success,
    bool Cancelled,
    BackendRunResult? BackendResult,
    string? ErrorMessage)
{
    public bool Failed => !Success && !Cancelled;
}
