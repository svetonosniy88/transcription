using System.Windows;

namespace WhisperMd.App.Notes;

public sealed class WpfNoteConflictResolver : INoteConflictResolver
{
    public Task<NoteConflictAction> ResolveAsync(string existingPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message =
            $"Markdown-файл уже существует:\n\n{existingPath}\n\n" +
            "Да — заменить существующий файл.\n" +
            "Нет — сохранить копию с новым именем.\n" +
            "Отмена — не сохранять эту заметку.";

        MessageBoxResult result;
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            result = dispatcher.Invoke(() => MessageBox.Show(
                message,
                "Конфликт Markdown-файла",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.No));
        }
        else
        {
            result = MessageBox.Show(
                message,
                "Конфликт Markdown-файла",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.No);
        }

        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => NoteConflictAction.Replace,
            MessageBoxResult.No => NoteConflictAction.SaveCopy,
            _ => NoteConflictAction.Cancel
        });
    }
}
