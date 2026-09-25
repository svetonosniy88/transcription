namespace WhisperMd.App.Notes;

public sealed record MarkdownNoteSaveResult(
    IReadOnlyList<SavedMarkdownNote> SavedNotes,
    IReadOnlyList<MarkdownNoteSaveFailure> Failures,
    int SkippedCount)
{
    public int SavedCount => SavedNotes.Count;

    public int FailedCount => Failures.Count;
}
