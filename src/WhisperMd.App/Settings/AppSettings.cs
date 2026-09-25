using System.IO;

namespace WhisperMd.App.Settings;

public sealed record AppSettings(
    string Language,
    string Backend,
    int Threads,
    string LibraryDirectory,
    string DefaultPrompt)
{
    public static AppSettings CreateDefault()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var library = string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WhisperMd")
            : Path.Combine(documents, "WhisperMd");

        return new AppSettings(
            Language: "ru",
            Backend: "auto",
            Threads: 8,
            LibraryDirectory: library,
            DefaultPrompt: string.Empty);
    }

    public AppSettings Normalize()
    {
        var defaults = CreateDefault();
        var backend = Backend?.Trim().ToLowerInvariant() switch
        {
            "cpu" => "cpu",
            "vulkan" => "vulkan",
            _ => "auto"
        };

        var language = string.IsNullOrWhiteSpace(Language) ? defaults.Language : Language.Trim().ToLowerInvariant();
        var threads = Math.Clamp(Threads, 1, 32);
        var library = string.IsNullOrWhiteSpace(LibraryDirectory)
            ? defaults.LibraryDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(LibraryDirectory.Trim()));

        return this with
        {
            Language = language,
            Backend = backend,
            Threads = threads,
            LibraryDirectory = library,
            DefaultPrompt = DefaultPrompt?.Trim() ?? string.Empty
        };
    }
}
