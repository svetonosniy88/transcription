using System.IO;

namespace WhisperMd.App.History;

public sealed record HistoryEntry(
    string Id,
    string Title,
    string FilePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int SourceCount,
    string Preview)
{
    public bool FileExists => File.Exists(FilePath);

    public string DateText => UpdatedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");

    public string SourceCountText => SourceCount switch
    {
        1 => "1 запись",
        var count when count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) => $"{count} записи",
        var count => $"{count} записей"
    };
}
