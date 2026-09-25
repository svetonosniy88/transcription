using WhisperMd.App.Models;

namespace WhisperMd.App.Execution;

public sealed record TranscriptionJobRunResult(
    TranscriptionJob Job,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<TranscriptionItemRunResult> Items)
{
    public int TotalCount => Items.Count;

    public int CompletedCount => Items.Count(item => item.Success);

    public int FailedCount => Items.Count(item => item.Failed);

    public int CancelledCount => Items.Count(item => item.Cancelled);

    public bool WasCancelled => CancelledCount > 0;

    public bool IsSuccessful => FailedCount == 0 && CancelledCount == 0;
}
