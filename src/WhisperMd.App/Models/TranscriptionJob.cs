namespace WhisperMd.App.Models;

/// <summary>
/// Immutable snapshot of the task assembled on the transcription screen.
/// The application execution layer consumes this snapshot without reading mutable UI state.
/// </summary>
public sealed record TranscriptionJob(
    IReadOnlyList<TranscriptionJobItem> Items,
    NoteMode NoteMode,
    string ContextPrompt,
    string CombinedNoteTitle,
    IReadOnlyList<TranscriptionGroupDefinition> Groups,
    string Backend = "auto",
    string Language = "ru",
    int Threads = 8,
    string? LibraryDirectory = null);

public sealed record TranscriptionJobItem(
    Guid RecordingId,
    string InputPath,
    int Order,
    Guid? GroupId);

public sealed record TranscriptionGroupDefinition(
    Guid Id,
    string Name);
