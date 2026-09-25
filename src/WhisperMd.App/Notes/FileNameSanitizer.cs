using System.IO;
using System.Text;

namespace WhisperMd.App.Notes;

internal static class FileNameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string? title)
    {
        var source = string.IsNullOrWhiteSpace(title) ? "Заметка" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);

        foreach (var ch in source)
            builder.Append(invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch);

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result))
            result = "Заметка";

        var deviceName = result.Split('.', 2)[0];
        if (ReservedNames.Contains(deviceName))
            result = $"_{result}";

        const int maxLength = 120;
        if (result.Length > maxLength)
            result = result[..maxLength].TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(result) ? "Заметка" : result;
    }
}
