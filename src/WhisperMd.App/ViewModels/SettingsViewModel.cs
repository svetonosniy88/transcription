using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using WhisperMd.App.Infrastructure;
using WhisperMd.App.Settings;

namespace WhisperMd.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettingsService _settingsService;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly AsyncRelayCommand _resetCommand;
    private readonly RelayCommand _browseLibraryCommand;
    private readonly RelayCommand _openLibraryCommand;

    private string _language;
    private string _backend;
    private string _recognitionMode;
    private int _threads;
    private string _libraryDirectory;
    private string _defaultPrompt;
    private string _statusText = "Настройки загружены.";
    private bool _isDirty;

    public SettingsViewModel(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = settingsService.Current;
        _language = settings.Language;
        _backend = settings.Backend;
        _recognitionMode = settings.RecognitionMode;
        _threads = settings.Threads;
        _libraryDirectory = settings.LibraryDirectory;
        _defaultPrompt = settings.DefaultPrompt;

        _saveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty);
        _resetCommand = new AsyncRelayCommand(ResetAsync);
        _browseLibraryCommand = new RelayCommand(_ => BrowseLibrary());
        _openLibraryCommand = new RelayCommand(_ => OpenLibrary(), _ => Directory.Exists(LibraryDirectory));
    }

    public IReadOnlyList<SettingOption> LanguageOptions { get; } = new[]
    {
        new SettingOption("ru", "Русский"),
        new SettingOption("en", "English"),
        new SettingOption("auto", "Автоопределение")
    };

    public IReadOnlyList<SettingOption> BackendOptions { get; } = new[]
    {
        new SettingOption("auto", "Auto — сначала Vulkan, затем CPU"),
        new SettingOption("vulkan", "Vulkan"),
        new SettingOption("cpu", "CPU")
    };

    public string Language
    {
        get => _language;
        set => SetAndMarkDirty(ref _language, value ?? "ru");
    }

    public IReadOnlyList<SettingOption> RecognitionModeOptions { get; } = new[]
    {
        new SettingOption("auto", "Авто — адаптивный поиск"),
        new SettingOption("standard", "Стандартный — 5 вариантов"),
        new SettingOption("balanced", "Умеренный — 6 вариантов"),
        new SettingOption("thorough", "Расширенный — 8 вариантов")
    };

    public string RecognitionMode
    {
        get => _recognitionMode;
        set
        {
            if (SetAndMarkDirty(ref _recognitionMode, value ?? "auto"))
                OnPropertyChanged(nameof(RecognitionModeDescription));
        }
    }

    public string RecognitionModeDescription => RecognitionMode switch
    {
        "standard" => "Прежняя глубина поиска: 5 вариантов и до 5 кандидатов при повторных попытках.",
        "balanced" => "Небольшое увеличение поиска: 6 вариантов и до 5 кандидатов при повторных попытках.",
        "thorough" => "Поиск по 8 вариантам на каждом фрагменте; может потребовать больше времени.",
        _ => "Поиск по 6 вариантам. Если движок не уверен в результате, повторные попытки используют до 8 кандидатов."
    };

    public string Backend
    {
        get => _backend;
        set => SetAndMarkDirty(ref _backend, value ?? "auto");
    }

    public int Threads
    {
        get => _threads;
        set => SetAndMarkDirty(ref _threads, Math.Clamp(value, 1, 32));
    }

    public string LibraryDirectory
    {
        get => _libraryDirectory;
        set
        {
            if (SetAndMarkDirty(ref _libraryDirectory, value ?? string.Empty))
                _openLibraryCommand.RaiseCanExecuteChanged();
        }
    }

    public string DefaultPrompt
    {
        get => _defaultPrompt;
        set => SetAndMarkDirty(ref _defaultPrompt, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value))
                return;
            _saveCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand SaveCommand => _saveCommand;
    public ICommand ResetCommand => _resetCommand;
    public ICommand BrowseLibraryCommand => _browseLibraryCommand;
    public ICommand OpenLibraryCommand => _openLibraryCommand;

    private async Task SaveAsync()
    {
        try
        {
            var settings = new AppSettings(Language, Backend, Threads, LibraryDirectory, DefaultPrompt, RecognitionMode);
            await _settingsService.SaveAsync(settings);
            Apply(_settingsService.Current);
            IsDirty = false;
            StatusText = "Настройки сохранены.";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            await _settingsService.ResetAsync();
            Apply(_settingsService.Current);
            IsDirty = false;
            StatusText = "Настройки сброшены к значениям по умолчанию.";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось сбросить настройки: {ex.Message}";
        }
    }

    private void BrowseLibrary()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку библиотеки Markdown",
            Multiselect = false,
            InitialDirectory = Directory.Exists(LibraryDirectory)
                ? LibraryDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
            LibraryDirectory = dialog.FolderName;
    }

    private void OpenLibrary()
    {
        if (!Directory.Exists(LibraryDirectory))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LibraryDirectory}\"") { UseShellExecute = true });
    }

    private void Apply(AppSettings settings)
    {
        _language = settings.Language;
        _backend = settings.Backend;
        _recognitionMode = settings.RecognitionMode;
        _threads = settings.Threads;
        _libraryDirectory = settings.LibraryDirectory;
        _defaultPrompt = settings.DefaultPrompt;

        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Backend));
        OnPropertyChanged(nameof(RecognitionMode));
        OnPropertyChanged(nameof(RecognitionModeDescription));
        OnPropertyChanged(nameof(Threads));
        OnPropertyChanged(nameof(LibraryDirectory));
        OnPropertyChanged(nameof(DefaultPrompt));
        _openLibraryCommand.RaiseCanExecuteChanged();
    }

    private bool SetAndMarkDirty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        var changed = SetProperty(ref field, value, propertyName);
        if (changed)
        {
            IsDirty = true;
            StatusText = "Есть несохранённые изменения.";
        }
        return changed;
    }
}
