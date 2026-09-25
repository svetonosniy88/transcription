using System.IO;
using System.Text;
using WhisperMd.App.Execution;
using WhisperMd.App.Models;

namespace WhisperMd.App.Notes;

public sealed class MarkdownNoteBuilder
{
    public async Task<IReadOnlyList<MarkdownNoteDraft>> BuildAsync(
        TranscriptionJobRunResult runResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runResult);

        var jobItems = runResult.Job.Items.OrderBy(item => item.Order).ToArray();
        var resultByRecordingId = runResult.Items.ToDictionary(item => item.RecordingId);
        var parts = new Dictionary<Guid, NotePart>();

        foreach (var item in jobItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            resultByRecordingId.TryGetValue(item.RecordingId, out var itemResult);
            parts[item.RecordingId] = await CreatePartAsync(item, itemResult, cancellationToken);
        }

        return runResult.Job.NoteMode switch
        {
            NoteMode.Separate => BuildSeparate(runResult.Job, jobItems, parts),
            NoteMode.Combined => BuildCombined(runResult.Job, jobItems, parts),
            NoteMode.CustomGroups => BuildCustomGroups(runResult.Job, jobItems, parts),
            _ => Array.Empty<MarkdownNoteDraft>()
        };
    }

    private static IReadOnlyList<MarkdownNoteDraft> BuildSeparate(
        TranscriptionJob job,
        IReadOnlyList<TranscriptionJobItem> jobItems,
        IReadOnlyDictionary<Guid, NotePart> parts)
    {
        var drafts = new List<MarkdownNoteDraft>();

        foreach (var item in jobItems)
        {
            var part = parts[item.RecordingId];
            if (!part.Success)
                continue;

            var title = Path.GetFileNameWithoutExtension(item.InputPath);
            drafts.Add(new MarkdownNoteDraft(
                title,
                GetOutputDirectory(job, item.InputPath),
                BuildSingleMarkdown(title, part.Text),
                new[] { item.RecordingId },
                new[] { item.InputPath }));
        }

        return drafts;
    }

    private static IReadOnlyList<MarkdownNoteDraft> BuildCombined(
        TranscriptionJob job,
        IReadOnlyList<TranscriptionJobItem> jobItems,
        IReadOnlyDictionary<Guid, NotePart> parts)
    {
        if (jobItems.Count == 0 || !jobItems.Any(item => parts[item.RecordingId].Success))
            return Array.Empty<MarkdownNoteDraft>();

        var title = string.IsNullOrWhiteSpace(job.CombinedNoteTitle)
            ? BuildCombinedFallbackTitle(jobItems)
            : job.CombinedNoteTitle.Trim();

        return new[]
        {
            BuildAggregateDraft(job, title, jobItems, parts)
        };
    }

    private static IReadOnlyList<MarkdownNoteDraft> BuildCustomGroups(
        TranscriptionJob job,
        IReadOnlyList<TranscriptionJobItem> jobItems,
        IReadOnlyDictionary<Guid, NotePart> parts)
    {
        var drafts = new List<MarkdownNoteDraft>();
        var knownGroupIds = job.Groups.Select(group => group.Id).ToHashSet();

        foreach (var group in job.Groups)
        {
            var groupedItems = jobItems.Where(item => item.GroupId == group.Id).ToArray();
            if (groupedItems.Length == 0 || !groupedItems.Any(item => parts[item.RecordingId].Success))
                continue;

            drafts.Add(BuildAggregateDraft(job, group.Name, groupedItems, parts));
        }

        var ungroupedItems = jobItems
            .Where(item => item.GroupId is null || !knownGroupIds.Contains(item.GroupId.Value))
            .ToArray();

        if (ungroupedItems.Length > 0 && ungroupedItems.Any(item => parts[item.RecordingId].Success))
        {
            var firstStem = Path.GetFileNameWithoutExtension(ungroupedItems[0].InputPath);
            var title = ungroupedItems.Length == 1 ? firstStem : $"{firstStem} — без группы";
            drafts.Add(BuildAggregateDraft(job, title, ungroupedItems, parts));
        }

        return drafts;
    }

    private static MarkdownNoteDraft BuildAggregateDraft(
        TranscriptionJob job,
        string title,
        IReadOnlyList<TranscriptionJobItem> items,
        IReadOnlyDictionary<Guid, NotePart> parts)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(title.Trim());
        builder.AppendLine();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var part = parts[item.RecordingId];
            var stem = Path.GetFileNameWithoutExtension(item.InputPath);

            builder.Append("## Часть ")
                .Append(index + 1)
                .Append(" — ")
                .AppendLine(stem);
            builder.AppendLine();

            if (part.Success)
            {
                builder.AppendLine(part.Text);
            }
            else
            {
                builder.Append("> Фрагмент не транскрибирован: ")
                    .AppendLine(part.ErrorMessage ?? "неизвестная ошибка.");
            }

            if (index < items.Count - 1)
            {
                builder.AppendLine();
                builder.AppendLine("---");
                builder.AppendLine();
            }
        }

        return new MarkdownNoteDraft(
            title.Trim(),
            GetOutputDirectory(job, items[0].InputPath),
            NormalizeMarkdown(builder.ToString()),
            items.Select(item => item.RecordingId).ToArray(),
            items.Select(item => item.InputPath).ToArray());
    }

    private static string BuildSingleMarkdown(string title, string text)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(title.Trim());
        builder.AppendLine();
        builder.AppendLine(text);
        return NormalizeMarkdown(builder.ToString());
    }

    private static string BuildCombinedFallbackTitle(IReadOnlyList<TranscriptionJobItem> items)
    {
        var firstStem = Path.GetFileNameWithoutExtension(items[0].InputPath);
        return items.Count == 1 ? firstStem : $"{firstStem} — объединено";
    }

    private static async Task<NotePart> CreatePartAsync(
        TranscriptionJobItem jobItem,
        TranscriptionItemRunResult? itemResult,
        CancellationToken cancellationToken)
    {
        if (itemResult is null)
            return NotePart.Failed("результат обработки отсутствует.");

        if (itemResult.Cancelled)
            return NotePart.Failed("обработка отменена пользователем.");

        if (!itemResult.Success || itemResult.BackendResult is null)
            return NotePart.Failed(itemResult.ErrorMessage ?? "ошибка транскрибации.");

        var txtPath = itemResult.BackendResult.ResultEvent.TxtPath;
        if (string.IsNullOrWhiteSpace(txtPath) || !File.Exists(txtPath))
            return NotePart.Failed("TXT-результат backend не найден.");

        try
        {
            var text = (await File.ReadAllTextAsync(txtPath, cancellationToken)).Trim();
            return NotePart.Completed(string.IsNullOrWhiteSpace(text) ? "_Транскрипция пуста._" : text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NotePart.Failed($"не удалось прочитать TXT: {ex.Message}");
        }
    }

    private static string GetOutputDirectory(TranscriptionJob job, string inputPath)
    {
        if (!string.IsNullOrWhiteSpace(job.LibraryDirectory))
            return Path.GetFullPath(job.LibraryDirectory);

        return Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
    }

    private static string NormalizeMarkdown(string markdown)
        => markdown.TrimEnd() + Environment.NewLine;

    private sealed record NotePart(bool Success, string Text, string? ErrorMessage)
    {
        public static NotePart Completed(string text) => new(true, text, null);

        public static NotePart Failed(string error) => new(false, string.Empty, error);
    }
}
