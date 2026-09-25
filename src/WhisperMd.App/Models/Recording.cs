using System.IO;
using WhisperMd.App.Infrastructure;

namespace WhisperMd.App.Models;

public sealed class Recording : ViewModelBase
{
    private Guid? _groupId;
    private RecordingStatus _status = RecordingStatus.Queued;
    private double _progress;
    private string? _errorMessage;
    private string? _activeBackend;

    public Recording(string fullPath)
    {
        FullPath = Path.GetFullPath(fullPath);
        FileName = Path.GetFileName(FullPath);
        Extension = Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();

        var info = new FileInfo(FullPath);
        FileSizeBytes = info.Exists ? info.Length : 0;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string FullPath { get; }

    public string FileName { get; }

    public string Extension { get; }

    public long FileSizeBytes { get; }

    public string FileSizeText => FormatFileSize(FileSizeBytes);

    public Guid? GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    public RecordingStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText
    {
        get
        {
            var baseText = Status switch
            {
                RecordingStatus.Queued => "Ожидает",
                RecordingStatus.Preparing => "Подготовка аудио",
                RecordingStatus.Transcribing => "Транскрибация",
                RecordingStatus.BuildingResult => "Проверка результата",
                RecordingStatus.Completed => "Готово",
                RecordingStatus.Failed => "Ошибка",
                RecordingStatus.Cancelled => "Отменено",
                _ => Status.ToString()
            };

            return Status == RecordingStatus.Transcribing && !string.IsNullOrWhiteSpace(ActiveBackend)
                ? $"{baseText} · {ActiveBackend}"
                : baseText;
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, Math.Clamp(value, 0, 100)))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText => $"{Progress:0}%";

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? ActiveBackend
    {
        get => _activeBackend;
        set
        {
            if (SetProperty(ref _activeBackend, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public void ResetExecutionState()
    {
        Status = RecordingStatus.Queued;
        Progress = 0;
        ErrorMessage = null;
        ActiveBackend = null;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} Б";

        var kb = bytes / 1024d;
        if (kb < 1024)
            return $"{kb:0.#} КБ";

        var mb = kb / 1024d;
        if (mb < 1024)
            return $"{mb:0.#} МБ";

        return $"{mb / 1024d:0.##} ГБ";
    }
}
