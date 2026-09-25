using System.Windows.Input;
using WhisperMd.App.Backend;
using WhisperMd.App.Execution;
using WhisperMd.App.History;
using WhisperMd.App.Infrastructure;
using WhisperMd.App.Notes;
using WhisperMd.App.Settings;

namespace WhisperMd.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly TranscriptionViewModel _transcription;
    private readonly HistoryViewModel _history;
    private readonly SettingsViewModel _settings;
    private readonly AppSettingsService _settingsService;

    private ViewModelBase _currentPage;
    private string _currentSection = "transcription";

    public MainViewModel()
    {
        _settingsService = new AppSettingsService();
        var historyService = new NoteHistoryService();

        IWhisperBackendClient backendClient = new WhisperBackendClient();
        var jobRunner = new TranscriptionJobRunner(backendClient);
        var noteService = new MarkdownNoteService(
            new MarkdownNoteBuilder(),
            new MarkdownNoteWriter(new WpfNoteConflictResolver()));

        _transcription = new TranscriptionViewModel(jobRunner, noteService, historyService, _settingsService);
        _history = new HistoryViewModel(historyService);
        _settings = new SettingsViewModel(_settingsService);

        _currentPage = _transcription;
        NavigateCommand = new RelayCommand(Navigate);
        _settingsService.SettingsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(BackendSummary));
            OnPropertyChanged(nameof(LanguageSummary));
        };
    }

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }

    public string BackendSummary => _settingsService.Current.Backend switch
    {
        "cpu" => "CPU",
        "vulkan" => "Vulkan",
        _ => "Auto · Vulkan → CPU"
    };

    public string LanguageSummary => _settingsService.Current.Language switch
    {
        "en" => "English",
        "auto" => "Auto language",
        _ => "Русский"
    };

    public ICommand NavigateCommand { get; }

    private void Navigate(object? parameter)
    {
        CurrentSection = parameter?.ToString()?.ToLowerInvariant() switch
        {
            "history" => "history",
            "settings" => "settings",
            _ => "transcription"
        };

        CurrentPage = CurrentSection switch
        {
            "history" => _history,
            "settings" => _settings,
            _ => _transcription
        };

        if (CurrentSection == "history")
            _ = _history.RefreshAsync();
    }
}
