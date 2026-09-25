namespace WhisperMd.App.Notes;

public sealed record MarkdownNoteSaveFailure(
    string Title,
    string IntendedPath,
    string ErrorMessage);
