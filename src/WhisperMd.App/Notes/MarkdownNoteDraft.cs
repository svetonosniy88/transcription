namespace WhisperMd.App.Notes;

public sealed record MarkdownNoteDraft(
    string Title,
    string DirectoryPath,
    string Markdown,
    IReadOnlyList<Guid> RecordingIds,
    IReadOnlyList<string> SourcePaths);
