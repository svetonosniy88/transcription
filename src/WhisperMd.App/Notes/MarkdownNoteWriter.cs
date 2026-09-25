using System.IO;
using System.Text;

namespace WhisperMd.App.Notes;

public sealed class MarkdownNoteWriter
{
    private readonly INoteConflictResolver _conflictResolver;

    public MarkdownNoteWriter(INoteConflictResolver conflictResolver)
    {
        _conflictResolver = conflictResolver;
    }

    public async Task<MarkdownNoteSaveResult> SaveAsync(
        IReadOnlyList<MarkdownNoteDraft> drafts,
        CancellationToken cancellationToken = default)
    {
        var saved = new List<SavedMarkdownNote>(drafts.Count);
        var failures = new List<MarkdownNoteSaveFailure>();
        var skipped = 0;

        foreach (var draft in drafts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeTitle = FileNameSanitizer.Sanitize(draft.Title);
            var targetPath = Path.Combine(draft.DirectoryPath, $"{safeTitle}.md");

            try
            {
                Directory.CreateDirectory(draft.DirectoryPath);

                if (File.Exists(targetPath))
                {
                    var action = await _conflictResolver.ResolveAsync(targetPath, cancellationToken);
                    if (action == NoteConflictAction.Cancel)
                    {
                        skipped++;
                        continue;
                    }

                    if (action == NoteConflictAction.SaveCopy)
                        targetPath = GetAvailableCopyPath(draft.DirectoryPath, safeTitle);
                }

                await WriteAtomicallyAsync(targetPath, draft.Markdown, cancellationToken);
                saved.Add(new SavedMarkdownNote(
                    draft.Title,
                    targetPath,
                    draft.RecordingIds,
                    draft.SourcePaths));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(new MarkdownNoteSaveFailure(
                    draft.Title,
                    targetPath,
                    string.IsNullOrWhiteSpace(ex.Message) ? "Не удалось сохранить Markdown-файл." : ex.Message));
            }
        }

        return new MarkdownNoteSaveResult(saved, failures, skipped);
    }

    private static string GetAvailableCopyPath(string directoryPath, string safeTitle)
    {
        for (var index = 2; index < int.MaxValue; index++)
        {
            var candidate = Path.Combine(directoryPath, $"{safeTitle} ({index}).md");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Не удалось подобрать свободное имя для Markdown-файла.");
    }

    private static async Task WriteAtomicallyAsync(
        string targetPath,
        string markdown,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException($"Не удалось определить папку результата: {targetPath}");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
