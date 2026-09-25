using WhisperMd.App.Backend;
using WhisperMd.App.Models;
using WhisperMd.App.Settings;

namespace WhisperMd.App.Execution;

/// <summary>
/// Executes an immutable TranscriptionJob sequentially.
/// It owns queue orchestration, while WhisperBackendClient remains responsible for one recording only.
/// </summary>
public sealed class TranscriptionJobRunner
{
    private readonly IWhisperBackendClient _backendClient;

    public TranscriptionJobRunner(IWhisperBackendClient backendClient)
    {
        _backendClient = backendClient;
    }

    public async Task<TranscriptionJobRunResult> RunAsync(
        TranscriptionJob job,
        IProgress<TranscriptionRunUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var startedAt = DateTimeOffset.Now;
        var orderedItems = job.Items.OrderBy(item => item.Order).ToArray();
        var results = new List<TranscriptionItemRunResult>(orderedItems.Length);

        for (var index = 0; index < orderedItems.Length; index++)
        {
            var item = orderedItems[index];

            if (cancellationToken.IsCancellationRequested)
            {
                AppendCancelledItems(orderedItems, index, results, progress);
                break;
            }

            progress?.Report(new TranscriptionRunUpdate(item.RecordingId, TranscriptionRunUpdateKind.ItemStarted));

            try
            {
                var backendResult = await _backendClient.RunAsync(
                    new BackendRunRequest(
                        item.InputPath,
                        Backend: job.Backend,
                        Language: job.Language,
                        Threads: job.Threads,
                        Prompt: job.ContextPrompt,
                        WorkingDirectory: AppDataPaths.RuntimeWorkingDirectory,
                        OutputDirectory: AppDataPaths.RuntimeOutputDirectory),
                    backendEvent => progress?.Report(new TranscriptionRunUpdate(
                        item.RecordingId,
                        TranscriptionRunUpdateKind.BackendEvent,
                        BackendEvent: backendEvent)),
                    cancellationToken: cancellationToken);

                var itemResult = new TranscriptionItemRunResult(
                    item.RecordingId,
                    item.InputPath,
                    item.Order,
                    Success: true,
                    Cancelled: false,
                    BackendResult: backendResult,
                    ErrorMessage: null);

                results.Add(itemResult);
                progress?.Report(new TranscriptionRunUpdate(item.RecordingId, TranscriptionRunUpdateKind.ItemCompleted));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(CreateCancelledResult(item));
                progress?.Report(new TranscriptionRunUpdate(
                    item.RecordingId,
                    TranscriptionRunUpdateKind.ItemCancelled,
                    Message: "Отменено пользователем."));

                AppendCancelledItems(orderedItems, index + 1, results, progress);
                break;
            }
            catch (Exception ex)
            {
                var message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Неизвестная ошибка транскрибации."
                    : ex.Message;

                results.Add(new TranscriptionItemRunResult(
                    item.RecordingId,
                    item.InputPath,
                    item.Order,
                    Success: false,
                    Cancelled: false,
                    BackendResult: null,
                    ErrorMessage: message));

                progress?.Report(new TranscriptionRunUpdate(
                    item.RecordingId,
                    TranscriptionRunUpdateKind.ItemFailed,
                    Message: message));
            }
        }

        return new TranscriptionJobRunResult(job, startedAt, DateTimeOffset.Now, results);
    }

    private static void AppendCancelledItems(
        IReadOnlyList<TranscriptionJobItem> orderedItems,
        int startIndex,
        ICollection<TranscriptionItemRunResult> results,
        IProgress<TranscriptionRunUpdate>? progress)
    {
        for (var index = startIndex; index < orderedItems.Count; index++)
        {
            var item = orderedItems[index];
            results.Add(CreateCancelledResult(item));
            progress?.Report(new TranscriptionRunUpdate(
                item.RecordingId,
                TranscriptionRunUpdateKind.ItemCancelled,
                Message: "Не запущено: очередь отменена пользователем."));
        }
    }

    private static TranscriptionItemRunResult CreateCancelledResult(TranscriptionJobItem item)
        => new(
            item.RecordingId,
            item.InputPath,
            item.Order,
            Success: false,
            Cancelled: true,
            BackendResult: null,
            ErrorMessage: null);
}
