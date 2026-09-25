using System.IO;

namespace WhisperMd.App.Settings;

public static class AppDataPaths
{
    public static string RootDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhisperMd");

    public static string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    public static string HistoryDatabasePath => Path.Combine(RootDirectory, "history.db");

    public static string RuntimeDirectory => Path.Combine(RootDirectory, "runtime");

    public static string RuntimeWorkingDirectory => Path.Combine(RuntimeDirectory, "working");

    public static string RuntimeOutputDirectory => Path.Combine(RuntimeDirectory, "output");
}
