using WhisperMd.App.Execution;

namespace WhisperMd.App.Notes;

public sealed class MarkdownNoteService
{
    private readonly MarkdownNoteBuilder _builder;
    private readonly MarkdownNoteWriter _writer;

    public MarkdownNoteService(MarkdownNoteBuilder builder, MarkdownNoteWriter writer)
    {
        _builder = builder;
        _writer = writer;
    }

    public async Task<MarkdownNoteSaveResult> BuildAndSaveAsync(
        TranscriptionJobRunResult runResult,
        CancellationToken cancellationToken = default)
    {
        if (runResult.WasCancelled || runResult.CompletedCount == 0)
            return new MarkdownNoteSaveResult(Array.Empty<SavedMarkdownNote>(), Array.Empty<MarkdownNoteSaveFailure>(), 0);

        var drafts = await _builder.BuildAsync(runResult, cancellationToken);
        if (drafts.Count == 0)
            return new MarkdownNoteSaveResult(Array.Empty<SavedMarkdownNote>(), Array.Empty<MarkdownNoteSaveFailure>(), 0);

        return await _writer.SaveAsync(drafts, cancellationToken);
    }
}
