using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using WhisperMd.App.Backend;
using WhisperMd.App.Execution;
using WhisperMd.App.Infrastructure;
using WhisperMd.App.History;
using WhisperMd.App.Models;
using WhisperMd.App.Notes;
using WhisperMd.App.Settings;

namespace WhisperMd.App.ViewModels;

public sealed class TranscriptionViewModel : ViewModelBase
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".mp4", ".webm", ".mkv", ".mov"
    };

    private readonly TranscriptionJobRunner _jobRunner;
    private readonly MarkdownNoteService _noteService;
    private readonly NoteHistoryService _historyService;
    private readonly AppSettingsService _settingsService;
    private readonly RelayCommand _removeRecordingCommand;
    private readonly RelayCommand _moveUpCommand;
    private readonly RelayCommand _moveDownCommand;
    private readonly RelayCommand _clearQueueCommand;
    private readonly RelayCommand _addGroupCommand;
    private readonly RelayCommand _removeGroupCommand;
    private readonly RelayCommand _setNoteModeCommand;
    private readonly AsyncRelayCommand _startTranscriptionCommand;
    private readonly RelayCommand _cancelTranscriptionCommand;
    private readonly RelayCommand _openSavedNoteCommand;
    private readonly RelayCommand _openSavedNoteFolderCommand;
    private readonly RelayCommand _useDefaultPromptCommand;
    private readonly RelayCommand _openLibraryCommand;
    private readonly DispatcherTimer _runTimer;
    private readonly Stopwatch _runStopwatch = new();

    private NoteMode _noteMode = NoteMode.Separate;
    private string _contextPrompt = string.Empty;
    private string _combinedNoteTitle = string.Empty;
    private string _queueMessage = "Добавьте записи, чтобы сформировать задачу транскрибации.";
    private int _groupSequence = 1;
    private bool _isRunning;
    private bool _isCancellationRequested;
    private bool _isSavingNotes;
    private double _overallProgress;
    private double _currentFileProgress;
    private string _elapsedText = "00:00";
    private string _estimatedRemainingText = "—";
    private string _currentItemText = string.Empty;
    private string _currentStageText = string.Empty;
    private Guid? _activeRecordingId;
    private CancellationTokenSource? _runCancellation;
    private TranscriptionJobRunResult? _lastRunResult;

    public TranscriptionViewModel(
        TranscriptionJobRunner jobRunner,
        MarkdownNoteService noteService,
        NoteHistoryService historyService,
        AppSettingsService settingsService)
    {
        _jobRunner = jobRunner;
        _noteService = noteService;
        _historyService = historyService;
        _settingsService = settingsService;
        _contextPrompt = _settingsService.Current.DefaultPrompt;

        Recordings.CollectionChanged += RecordingsOnCollectionChanged;
        Groups.CollectionChanged += GroupsOnCollectionChanged;
        SavedNotes.CollectionChanged += SavedNotesOnCollectionChanged;

        _removeRecordingCommand = new RelayCommand(RemoveRecording, CanUseRecording);
        _moveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
        _moveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
        _clearQueueCommand = new RelayCommand(_ => ClearQueue(), _ => CanEditJob && HasRecordings);
        _addGroupCommand = new RelayCommand(_ => AddGroup(), _ => CanEditJob && IsCustomGroupsMode);
        _removeGroupCommand = new RelayCommand(RemoveGroup, parameter => CanEditJob && IsCustomGroupsMode && parameter is RecordingGroup);
        _setNoteModeCommand = new RelayCommand(SetNoteMode, _ => CanEditJob);
        _startTranscriptionCommand = new AsyncRelayCommand(StartTranscriptionAsync, () => CanStart);
        _cancelTranscriptionCommand = new RelayCommand(_ => CancelTranscription(), _ => CanCancel);
        _openSavedNoteCommand = new RelayCommand(OpenSavedNote, parameter => parameter is SavedMarkdownNote note && File.Exists(note.FilePath));
        _openSavedNoteFolderCommand = new RelayCommand(OpenSavedNoteFolder, parameter => parameter is SavedMarkdownNote note && File.Exists(note.FilePath));
        _useDefaultPromptCommand = new RelayCommand(_ => ContextPrompt = _settingsService.Current.DefaultPrompt, _ => CanEditJob);
        _openLibraryCommand = new RelayCommand(_ => OpenLibrary(), _ => Directory.Exists(_settingsService.Current.LibraryDirectory));

        _runTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _runTimer.Tick += (_, _) => RefreshRunTiming();

        _settingsService.SettingsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RunSettingsSummary));
            OnPropertyChanged(nameof(LibraryDirectory));
            _openLibraryCommand.RaiseCanExecuteChanged();
        };
    }

    public ObservableCollection<Recording> Recordings { get; } = new();

    public ObservableCollection<RecordingGroup> Groups { get; } = new();

    public ObservableCollection<SavedMarkdownNote> SavedNotes { get; } = new();

    public NoteMode NoteMode
    {
        get => _noteMode;
        private set
        {
            if (!SetProperty(ref _noteMode, value))
                return;

            OnPropertyChanged(nameof(IsSeparateMode));
            OnPropertyChanged(nameof(IsCombinedMode));
            OnPropertyChanged(nameof(IsCustomGroupsMode));
            OnPropertyChanged(nameof(NoteModeDescription));
            RefreshCommands();
        }
    }

    public bool IsSeparateMode => NoteMode == NoteMode.Separate;

    public bool IsCombinedMode => NoteMode == NoteMode.Combined;

    public bool IsCustomGroupsMode => NoteMode == NoteMode.CustomGroups;

    public string NoteModeDescription => NoteMode switch
    {
        NoteMode.Separate => "Каждый аудиофайл станет отдельной Markdown-заметкой.",
        NoteMode.Combined => "Все записи будут объединены в одну заметку в порядке очереди.",
        NoteMode.CustomGroups => "Записи одной группы станут одной заметкой. Файлы без группы образуют отдельную заметку.",
        _ => string.Empty
    };

    public string ContextPrompt
    {
        get => _contextPrompt;
        set => SetProperty(ref _contextPrompt, value ?? string.Empty);
    }

    public string CombinedNoteTitle
    {
        get => _combinedNoteTitle;
        set => SetProperty(ref _combinedNoteTitle, value ?? string.Empty);
    }

    public string QueueMessage
    {
        get => _queueMessage;
        private set => SetProperty(ref _queueMessage, value);
    }

    public bool HasRecordings => Recordings.Count > 0;

    public bool IsQueueEmpty => !HasRecordings;

    public int RecordingCount => Recordings.Count;

    public bool HasSavedNotes => SavedNotes.Count > 0;

    public string SavedNotesSummary => SavedNotes.Count switch
    {
        0 => string.Empty,
        1 => "Сохранена 1 Markdown-заметка.",
        var count when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => $"Сохранено {count} Markdown-заметки.",
        var count => $"Сохранено {count} Markdown-заметок."
    };

    public string QueueCountText => RecordingCount switch
    {
        0 => "0 файлов",
        1 => "1 файл",
        var count when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => $"{count} файла",
        var count => $"{count} файлов"
    };

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanEditJob));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(CancelButtonText));
            RefreshCommands();
        }
    }

    public bool CanEditJob => !IsRunning;

    public bool CanStart => HasRecordings && !IsRunning;

    public string StartButtonText => IsSavingNotes ? "Сохранение Markdown…" : IsRunning ? "Транскрибация…" : "Начать транскрибацию";

    public bool IsCancellationRequested
    {
        get => _isCancellationRequested;
        private set
        {
            if (!SetProperty(ref _isCancellationRequested, value))
                return;

            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelButtonText));
            _cancelTranscriptionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanCancel => IsRunning && !IsCancellationRequested && !IsSavingNotes;

    private bool IsSavingNotes
    {
        get => _isSavingNotes;
        set
        {
            if (!SetProperty(ref _isSavingNotes, value))
                return;

            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StartButtonText));
            _cancelTranscriptionCommand.RaiseCanExecuteChanged();
        }
    }

    public string CancelButtonText => IsCancellationRequested ? "Отмена…" : "Отменить";

    public double OverallProgress
    {
        get => _overallProgress;
        private set
        {
            if (SetProperty(ref _overallProgress, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(OverallProgressText));
                RefreshEstimatedRemaining();
            }
        }
    }

    public string OverallProgressText => $"{OverallProgress:0}%";

    public double CurrentFileProgress
    {
        get => _currentFileProgress;
        private set
        {
            if (SetProperty(ref _currentFileProgress, Math.Clamp(value, 0, 100)))
                OnPropertyChanged(nameof(CurrentFileProgressText));
        }
    }

    public string CurrentFileProgressText => $"{CurrentFileProgress:0}%";

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string EstimatedRemainingText
    {
        get => _estimatedRemainingText;
        private set => SetProperty(ref _estimatedRemainingText, value);
    }

    public string CurrentItemText
    {
        get => _currentItemText;
        private set => SetProperty(ref _currentItemText, value);
    }

    public string CurrentStageText
    {
        get => _currentStageText;
        private set => SetProperty(ref _currentStageText, value);
    }

    public TranscriptionJobRunResult? LastRunResult
    {
        get => _lastRunResult;
        private set => SetProperty(ref _lastRunResult, value);
    }

    public ICommand RemoveRecordingCommand => _removeRecordingCommand;

    public ICommand MoveUpCommand => _moveUpCommand;

    public ICommand MoveDownCommand => _moveDownCommand;

    public ICommand ClearQueueCommand => _clearQueueCommand;

    public ICommand AddGroupCommand => _addGroupCommand;

    public ICommand RemoveGroupCommand => _removeGroupCommand;

    public ICommand SetNoteModeCommand => _setNoteModeCommand;

    public ICommand StartTranscriptionCommand => _startTranscriptionCommand;

    public ICommand CancelTranscriptionCommand => _cancelTranscriptionCommand;

    public ICommand OpenSavedNoteCommand => _openSavedNoteCommand;

    public ICommand OpenSavedNoteFolderCommand => _openSavedNoteFolderCommand;

    public ICommand UseDefaultPromptCommand => _useDefaultPromptCommand;

    public ICommand OpenLibraryCommand => _openLibraryCommand;

    public string RunSettingsSummary
    {
        get
        {
            var settings = _settingsService.Current;
            var backend = settings.Backend switch
            {
                "cpu" => "CPU",
                "vulkan" => "Vulkan",
                _ => "Auto"
            };
            return $"{settings.Language} · {backend} · {settings.Threads} потоков";
        }
    }

    public string LibraryDirectory => _settingsService.Current.LibraryDirectory;

    public AddFilesResult AddFiles(IEnumerable<string> paths)
    {
        if (!CanEditJob)
        {
            QueueMessage = "Дождитесь завершения текущей очереди перед изменением списка файлов.";
            return default;
        }

        var added = 0;
        var duplicates = 0;
        var unsupported = 0;
        var missing = 0;

        var existing = new HashSet<string>(Recordings.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch
            {
                missing++;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                missing++;
                continue;
            }

            if (!SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            {
                unsupported++;
                continue;
            }

            if (!existing.Add(fullPath))
            {
                duplicates++;
                continue;
            }

            Recordings.Add(new Recording(fullPath));
            added++;
        }

        QueueMessage = BuildAddFilesMessage(added, duplicates, unsupported, missing);
        return new AddFilesResult(added, duplicates, unsupported, missing);
    }

    public TranscriptionJob CreateJobSnapshot()
    {
        var items = Recordings
            .Select((recording, index) => new TranscriptionJobItem(
                recording.Id,
                recording.FullPath,
                index,
                recording.GroupId))
            .ToArray();

        var groups = Groups
            .Select(group => new TranscriptionGroupDefinition(group.Id, group.Name))
            .ToArray();

        var settings = _settingsService.Current;
        return new TranscriptionJob(
            items,
            NoteMode,
            ContextPrompt.Trim(),
            CombinedNoteTitle.Trim(),
            groups,
            settings.Backend,
            settings.Language,
            settings.Threads,
            settings.LibraryDirectory);
    }

    public void MoveRecording(Recording source, Recording? target)
    {
        if (!CanEditJob || !Recordings.Contains(source))
            return;

        var oldIndex = Recordings.IndexOf(source);
        var newIndex = target is null ? Recordings.Count - 1 : Recordings.IndexOf(target);

        if (newIndex < 0 || oldIndex == newIndex)
            return;

        Recordings.Move(oldIndex, newIndex);
        QueueMessage = $"«{source.FileName}» перемещён в очереди.";
        RefreshCommands();
    }

    private async Task StartTranscriptionAsync()
    {
        if (!CanStart)
            return;

        var job = CreateJobSnapshot();
        if (job.Items.Count == 0)
            return;

        foreach (var recording in Recordings)
            recording.ResetExecutionState();

        LastRunResult = null;
        SavedNotes.Clear();
        _activeRecordingId = null;
        OverallProgress = 0;
        CurrentFileProgress = 0;
        CurrentItemText = string.Empty;
        CurrentStageText = "Подготовка очереди";
        ElapsedText = "00:00";
        EstimatedRemainingText = "—";

        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        IsCancellationRequested = false;

        _runStopwatch.Restart();
        _runTimer.Start();

        IsRunning = true;
        QueueMessage = $"Запущена очередь: {job.Items.Count} {FormatRecordingWord(job.Items.Count)}.";

        var progress = new Progress<TranscriptionRunUpdate>(ApplyRunUpdate);

        try
        {
            var result = await _jobRunner.RunAsync(job, progress, _runCancellation.Token);
            LastRunResult = result;
            RecalculateOverallProgress();

            if (result.WasCancelled)
            {
                QueueMessage = $"Очередь отменена: готово {result.CompletedCount}, с ошибкой {result.FailedCount}, отменено {result.CancelledCount}. Markdown не формировался.";
                CurrentStageText = "Отменено";
            }
            else
            {
                CurrentStageText = "Формирование Markdown";
                IsSavingNotes = true;
                MarkdownNoteSaveResult noteSaveResult;
                try
                {
                    noteSaveResult = await _noteService.BuildAndSaveAsync(result);
                }
                finally
                {
                    IsSavingNotes = false;
                }

                foreach (var note in noteSaveResult.SavedNotes)
                    SavedNotes.Add(note);

                CurrentStageText = "Обновление истории";
                var historyFailures = await IndexSavedNotesAsync(noteSaveResult.SavedNotes, job.ContextPrompt);

                if (result.FailedCount == 0 && noteSaveResult.FailedCount == 0)
                {
                    QueueMessage = BuildSuccessfulRunMessage(result, noteSaveResult);
                    CurrentStageText = noteSaveResult.SkippedCount == 0 ? "Готово" : "Готово, часть заметок пропущена";
                }
                else
                {
                    QueueMessage = BuildPartialRunMessage(result, noteSaveResult);
                    CurrentStageText = "Завершено с ошибками";
                }

                if (historyFailures > 0)
                    QueueMessage += $" Не удалось добавить в историю: {historyFailures}.";
            }
        }
        catch (Exception ex)
        {
            QueueMessage = $"Не удалось выполнить очередь: {ex.Message}";
            CurrentStageText = "Ошибка очереди";
        }
        finally
        {
            _runTimer.Stop();
            _runStopwatch.Stop();
            RefreshRunTiming();
            _activeRecordingId = null;
            IsSavingNotes = false;
            IsRunning = false;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    private void CancelTranscription()
    {
        if (!CanCancel || _runCancellation is null)
            return;

        IsCancellationRequested = true;
        CurrentStageText = "Остановка процессов…";
        QueueMessage = "Отмена запрошена. Завершаю текущий процесс и не запускаю следующие записи.";
        EstimatedRemainingText = "—";
        _runCancellation.Cancel();
    }

    private void ApplyRunUpdate(TranscriptionRunUpdate update)
    {
        var recording = Recordings.FirstOrDefault(item => item.Id == update.RecordingId);
        if (recording is null)
            return;

        switch (update.Kind)
        {
            case TranscriptionRunUpdateKind.ItemStarted:
                _activeRecordingId = recording.Id;
                recording.Status = RecordingStatus.Preparing;
                recording.Progress = 0;
                recording.ErrorMessage = null;
                recording.ActiveBackend = null;
                CurrentItemText = BuildCurrentItemText(recording);
                CurrentStageText = recording.StatusText;
                break;

            case TranscriptionRunUpdateKind.BackendEvent when update.BackendEvent is not null:
                ApplyBackendEvent(recording, update.BackendEvent);
                break;

            case TranscriptionRunUpdateKind.ItemCompleted:
                recording.Status = RecordingStatus.Completed;
                recording.Progress = 100;
                recording.ErrorMessage = null;
                break;

            case TranscriptionRunUpdateKind.ItemFailed:
                recording.Status = RecordingStatus.Failed;
                recording.Progress = 100;
                recording.ErrorMessage = update.Message ?? "Неизвестная ошибка транскрибации.";
                break;

            case TranscriptionRunUpdateKind.ItemCancelled:
                recording.Status = RecordingStatus.Cancelled;
                recording.ErrorMessage = null;
                break;
        }

        if (_activeRecordingId == recording.Id)
        {
            CurrentItemText = BuildCurrentItemText(recording);
            CurrentStageText = recording.StatusText;
            CurrentFileProgress = recording.Progress;
        }

        RecalculateOverallProgress();
        UpdateRunQueueMessage(recording);
    }

    private static void ApplyBackendEvent(Recording recording, WhisperBackendEvent backendEvent)
    {
        switch (backendEvent.Kind)
        {
            case WhisperBackendEventKind.Stage:
                recording.Status = backendEvent.Stage?.Trim().ToLowerInvariant() switch
                {
                    "preparing" => RecordingStatus.Preparing,
                    "transcribing" => RecordingStatus.Transcribing,
                    "validating" => RecordingStatus.BuildingResult,
                    "completed" => recording.Status,
                    _ => recording.Status
                };
                break;

            case WhisperBackendEventKind.Progress when backendEvent.Value is not null:
                recording.Progress = backendEvent.Value.Value;
                break;

            case WhisperBackendEventKind.BackendSelected:
                recording.ActiveBackend = FormatBackendName(backendEvent.Backend);
                break;

            case WhisperBackendEventKind.Fallback:
                recording.ActiveBackend = FormatBackendName(backendEvent.ToBackend);
                break;

            case WhisperBackendEventKind.Error:
                recording.ErrorMessage = backendEvent.Message;
                break;
        }
    }

    private void UpdateRunQueueMessage(Recording activeRecording)
    {
        if (!IsRunning)
            return;

        var processed = Recordings.Count(item =>
            item.Status is RecordingStatus.Completed or RecordingStatus.Failed or RecordingStatus.Cancelled);

        if (IsCancellationRequested)
        {
            QueueMessage = $"Отмена очереди… Обработано {processed} из {Recordings.Count}.";
            return;
        }

        QueueMessage = $"Обработано {processed} из {Recordings.Count}. Сейчас: {activeRecording.FileName} — {activeRecording.StatusText}.";
    }

    private string BuildCurrentItemText(Recording recording)
    {
        var index = Recordings.IndexOf(recording);
        return index >= 0
            ? $"Файл {index + 1} из {Recordings.Count} · {recording.FileName}"
            : recording.FileName;
    }

    private void RecalculateOverallProgress()
    {
        if (Recordings.Count == 0)
        {
            OverallProgress = 0;
            return;
        }

        var total = Recordings.Sum(recording => recording.Status switch
        {
            RecordingStatus.Completed => 100d,
            RecordingStatus.Failed => 100d,
            RecordingStatus.Cancelled => recording.Progress,
            _ => recording.Progress
        });

        OverallProgress = total / Recordings.Count;
    }

    private void RefreshRunTiming()
    {
        var elapsed = _runStopwatch.Elapsed;
        ElapsedText = FormatDuration(elapsed);
        RefreshEstimatedRemaining();
    }

    private void RefreshEstimatedRemaining()
    {
        if (!IsRunning || IsCancellationRequested || OverallProgress < 2 || _runStopwatch.Elapsed.TotalSeconds < 3)
        {
            EstimatedRemainingText = "—";
            return;
        }

        var remainingSeconds = _runStopwatch.Elapsed.TotalSeconds * (100d - OverallProgress) / OverallProgress;
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds < 0)
        {
            EstimatedRemainingText = "—";
            return;
        }

        EstimatedRemainingText = $"≈ {FormatDuration(TimeSpan.FromSeconds(remainingSeconds))}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void RemoveRecording(object? parameter)
    {
        if (!CanEditJob || parameter is not Recording recording)
            return;

        Recordings.Remove(recording);
        QueueMessage = $"«{recording.FileName}» удалён из очереди.";
    }

    private bool CanUseRecording(object? parameter)
        => CanEditJob && parameter is Recording;

    private void MoveUp(object? parameter)
    {
        if (!CanEditJob || parameter is not Recording recording)
            return;

        var index = Recordings.IndexOf(recording);
        if (index <= 0)
            return;

        Recordings.Move(index, index - 1);
        QueueMessage = $"«{recording.FileName}» поднят выше.";
        RefreshCommands();
    }

    private bool CanMoveUp(object? parameter)
        => CanEditJob && parameter is Recording recording && Recordings.IndexOf(recording) > 0;

    private void MoveDown(object? parameter)
    {
        if (!CanEditJob || parameter is not Recording recording)
            return;

        var index = Recordings.IndexOf(recording);
        if (index < 0 || index >= Recordings.Count - 1)
            return;

        Recordings.Move(index, index + 1);
        QueueMessage = $"«{recording.FileName}» опущен ниже.";
        RefreshCommands();
    }

    private bool CanMoveDown(object? parameter)
    {
        if (!CanEditJob || parameter is not Recording recording)
            return false;

        var index = Recordings.IndexOf(recording);
        return index >= 0 && index < Recordings.Count - 1;
    }

    private void ClearQueue()
    {
        if (!CanEditJob)
            return;

        Recordings.Clear();
        LastRunResult = null;
        SavedNotes.Clear();
        QueueMessage = "Очередь очищена.";
    }

    private void AddGroup()
    {
        if (!CanEditJob)
            return;

        var group = new RecordingGroup($"Группа {_groupSequence++}");
        Groups.Add(group);
        QueueMessage = $"Создана «{group.Name}». Название можно изменить.";
    }

    private void RemoveGroup(object? parameter)
    {
        if (!CanEditJob || parameter is not RecordingGroup group)
            return;

        foreach (var recording in Recordings.Where(r => r.GroupId == group.Id))
            recording.GroupId = null;

        Groups.Remove(group);
        QueueMessage = $"«{group.Name}» удалена. Её записи оставлены без группы.";
    }

    private void SetNoteMode(object? parameter)
    {
        if (!CanEditJob || parameter is null || !Enum.TryParse<NoteMode>(parameter.ToString(), ignoreCase: true, out var mode))
            return;

        NoteMode = mode;

        if (mode == NoteMode.CustomGroups && Groups.Count == 0)
            AddGroup();
    }

    private void RecordingsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasRecordings));
        OnPropertyChanged(nameof(IsQueueEmpty));
        OnPropertyChanged(nameof(RecordingCount));
        OnPropertyChanged(nameof(QueueCountText));
        OnPropertyChanged(nameof(CanStart));
        RefreshCommands();
    }

    private void GroupsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        _removeRecordingCommand.RaiseCanExecuteChanged();
        _moveUpCommand.RaiseCanExecuteChanged();
        _moveDownCommand.RaiseCanExecuteChanged();
        _clearQueueCommand.RaiseCanExecuteChanged();
        _addGroupCommand.RaiseCanExecuteChanged();
        _removeGroupCommand.RaiseCanExecuteChanged();
        _setNoteModeCommand.RaiseCanExecuteChanged();
        _startTranscriptionCommand.RaiseCanExecuteChanged();
        _cancelTranscriptionCommand.RaiseCanExecuteChanged();
        _useDefaultPromptCommand.RaiseCanExecuteChanged();
        _openSavedNoteCommand.RaiseCanExecuteChanged();
        _openSavedNoteFolderCommand.RaiseCanExecuteChanged();
        _openLibraryCommand.RaiseCanExecuteChanged();
    }

    private void SavedNotesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSavedNotes));
        OnPropertyChanged(nameof(SavedNotesSummary));
        _openSavedNoteCommand.RaiseCanExecuteChanged();
        _openSavedNoteFolderCommand.RaiseCanExecuteChanged();
    }

    private static string BuildSuccessfulRunMessage(TranscriptionJobRunResult result, MarkdownNoteSaveResult notes)
    {
        var message = notes.SavedCount > 0
            ? $"Готово: обработано {result.CompletedCount} из {result.TotalCount}; Markdown-заметок сохранено: {notes.SavedCount}."
            : $"Готово: обработано {result.CompletedCount} из {result.TotalCount}, но Markdown не сохранён.";

        if (notes.SkippedCount > 0)
            message += $" Пропущено по выбору пользователя: {notes.SkippedCount}.";

        return message;
    }

    private static string BuildPartialRunMessage(TranscriptionJobRunResult result, MarkdownNoteSaveResult notes)
    {
        var message = result.FailedCount > 0
            ? $"Очередь завершена: успешно {result.CompletedCount}, с ошибкой {result.FailedCount}; Markdown-заметок сохранено: {notes.SavedCount}."
            : $"Транскрибация завершена: успешно {result.CompletedCount}; Markdown-заметок сохранено: {notes.SavedCount}.";

        if (notes.FailedCount > 0)
        {
            message += $" Ошибок сохранения Markdown: {notes.FailedCount}.";
            var firstFailure = notes.Failures[0];
            message += $" Первая: {firstFailure.Title} — {firstFailure.ErrorMessage}";
        }

        if (notes.SkippedCount > 0)
            message += $" Пропущено по выбору пользователя: {notes.SkippedCount}.";

        return message;
    }

    private static string? FormatBackendName(string? backend)
        => backend?.Trim().ToLowerInvariant() switch
        {
            "vulkan" => "Vulkan",
            "cpu" => "CPU",
            null or "" => null,
            _ => backend
        };

    private static string FormatRecordingWord(int count)
        => count == 1
            ? "запись"
            : count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14)
                ? "записи"
                : "записей";

    private async Task<int> IndexSavedNotesAsync(IReadOnlyList<SavedMarkdownNote> notes, string prompt)
    {
        var failures = 0;
        foreach (var note in notes)
        {
            try
            {
                await _historyService.IndexAsync(note, prompt);
            }
            catch
            {
                failures++;
            }
        }

        return failures;
    }

    private static void OpenSavedNote(object? parameter)
    {
        if (parameter is not SavedMarkdownNote note || !File.Exists(note.FilePath))
            return;

        Process.Start(new ProcessStartInfo(note.FilePath) { UseShellExecute = true });
    }

    private static void OpenSavedNoteFolder(object? parameter)
    {
        if (parameter is not SavedMarkdownNote note || !File.Exists(note.FilePath))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{note.FilePath}\"") { UseShellExecute = true });
    }

    private void OpenLibrary()
    {
        var directory = _settingsService.Current.LibraryDirectory;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    private static string BuildAddFilesMessage(int added, int duplicates, int unsupported, int missing)
    {
        if (added == 0 && duplicates == 0 && unsupported == 0 && missing == 0)
            return "Файлы не выбраны.";

        var parts = new List<string>();
        if (added > 0)
            parts.Add($"добавлено: {added}");
        if (duplicates > 0)
            parts.Add($"дубликатов пропущено: {duplicates}");
        if (unsupported > 0)
            parts.Add($"неподдерживаемых форматов: {unsupported}");
        if (missing > 0)
            parts.Add($"недоступных файлов: {missing}");

        return char.ToUpperInvariant(parts[0][0]) + parts[0][1..] +
               (parts.Count > 1 ? "; " + string.Join("; ", parts.Skip(1)) : string.Empty) + ".";
    }
}

public readonly record struct AddFilesResult(int Added, int Duplicates, int Unsupported, int Missing);
