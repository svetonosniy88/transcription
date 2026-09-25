namespace WhisperMd.App.Notes;

public sealed record SavedMarkdownNote(
    string Title,
    string FilePath,
    IReadOnlyList<Guid> RecordingIds,
    IReadOnlyList<string> SourcePaths);
