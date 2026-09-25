using System.IO;
using System.Text.Json;

namespace WhisperMd.App.Settings;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private AppSettings _current;

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppDataPaths.SettingsFilePath;
        _current = LoadCore();
    }

    public AppSettings Current => _current;

    public event EventHandler? SettingsChanged;

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new IOException($"Не удалось определить папку настроек: {_settingsPath}");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(normalized.LibraryDirectory);

        var tempPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);

            File.Move(tempPath, _settingsPath, overwrite: true);
            _current = normalized;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
        => await SaveAsync(AppSettings.CreateDefault(), cancellationToken);

    private AppSettings LoadCore()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return AppSettings.CreateDefault().Normalize();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return (settings ?? AppSettings.CreateDefault()).Normalize();
        }
        catch
        {
            return AppSettings.CreateDefault().Normalize();
        }
    }
}
