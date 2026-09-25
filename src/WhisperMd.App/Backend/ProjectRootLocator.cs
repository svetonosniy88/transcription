using System.IO;

namespace WhisperMd.App.Backend;

public static class ProjectRootLocator
{
    public static string FindProjectRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("WHISPERMD_ROOT");
        if (IsProjectRoot(configuredRoot))
            return Path.GetFullPath(configuredRoot!);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (IsProjectRoot(directory.FullName))
                return directory.FullName;
        }

        const string localFallback = @"C:\Base\Transcription";
        return localFallback;
    }

    public static bool IsProjectRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(Path.Combine(path, "transcribe.ps1")) &&
               File.Exists(Path.Combine(path, "models", "ggml-small.bin"));
    }
}
