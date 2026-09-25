using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using WhisperMd.App.History;
using WhisperMd.App.Infrastructure;

namespace WhisperMd.App.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    private readonly NoteHistoryService _historyService;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly RelayCommand _openNoteCommand;
    private readonly RelayCommand _openFolderCommand;
    private readonly RelayCommand _removeCommand;
    private CancellationTokenSource? _searchDebounce;
    private string _searchText = string.Empty;
    private string _statusText = "Загрузка истории…";
    private bool _isLoading;

    public HistoryViewModel(NoteHistoryService historyService)
    {
        _historyService = historyService;
        _historyService.HistoryChanged += (_, _) => _ = DebouncedRefreshAsync();

        _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        _openNoteCommand = new RelayCommand(OpenNote, item => item is HistoryEntry entry && File.Exists(entry.FilePath));
        _openFolderCommand = new RelayCommand(OpenFolder, item => item is HistoryEntry entry && File.Exists(entry.FilePath));
        _removeCommand = new RelayCommand(item => _ = RemoveAsync(item as HistoryEntry), item => item is HistoryEntry);

        _ = RefreshAsync();
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
                return;

            _ = DebouncedRefreshAsync();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;

            OnPropertyChanged(nameof(IsEmpty));
            _refreshCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasEntries => Entries.Count > 0;

    public bool IsEmpty => !IsLoading && !HasEntries;

    public string CountText => Entries.Count switch
    {
        0 => "0 заметок",
        1 => "1 заметка",
        var count when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => $"{count} заметки",
        var count => $"{count} заметок"
    };

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand OpenNoteCommand => _openNoteCommand;
    public ICommand OpenFolderCommand => _openFolderCommand;
    public ICommand RemoveCommand => _removeCommand;

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusText = "Обновление истории…";
        try
        {
            var entries = await _historyService.SearchAsync(SearchText);
            Entries.Clear();
            foreach (var entry in entries)
                Entries.Add(entry);

            StatusText = string.IsNullOrWhiteSpace(SearchText)
                ? CountText
                : $"Найдено: {CountText}";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось открыть историю: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CountText));
        }
    }

    private async Task DebouncedRefreshAsync()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;

        try
        {
            await Task.Delay(250, token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void OpenNote(object? parameter)
    {
        if (parameter is not HistoryEntry entry || !File.Exists(entry.FilePath))
            return;

        Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
    }

    private static void OpenFolder(object? parameter)
    {
        if (parameter is not HistoryEntry entry || !File.Exists(entry.FilePath))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FilePath}\"") { UseShellExecute = true });
    }

    private async Task RemoveAsync(HistoryEntry? entry)
    {
        if (entry is null)
            return;

        try
        {
            await _historyService.RemoveAsync(entry.Id);
            StatusText = "Запись удалена из истории. Markdown-файл оставлен на диске.";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось удалить запись из истории: {ex.Message}";
        }
    }
}
