using WhisperMd.App.Backend;

namespace WhisperMd.App.Execution;

public enum TranscriptionRunUpdateKind
{
    ItemStarted,
    BackendEvent,
    ItemCompleted,
    ItemFailed,
    ItemCancelled
}

/// <summary>
/// Application-level queue update. The runner reports these independently of any UI framework.
/// </summary>
public sealed record TranscriptionRunUpdate(
    Guid RecordingId,
    TranscriptionRunUpdateKind Kind,
    WhisperBackendEvent? BackendEvent = null,
    string? Message = null);
