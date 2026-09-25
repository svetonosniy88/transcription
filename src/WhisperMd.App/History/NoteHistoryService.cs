using System.IO;
using Microsoft.Data.Sqlite;
using WhisperMd.App.Notes;
using WhisperMd.App.Settings;

namespace WhisperMd.App.History;

public sealed class NoteHistoryService
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public NoteHistoryService(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppDataPaths.HistoryDatabasePath;
    }

    public event EventHandler? HistoryChanged;

    public async Task IndexAsync(
        SavedMarkdownNote note,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        await EnsureInitializedAsync(cancellationToken);

        var markdown = File.Exists(note.FilePath)
            ? await File.ReadAllTextAsync(note.FilePath, cancellationToken)
            : string.Empty;
        var now = DateTimeOffset.Now;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existingId = await FindIdByPathAsync(connection, transaction, note.FilePath, cancellationToken);
        var id = existingId ?? Guid.NewGuid().ToString("N");

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = existingId is null
                ? """
                  INSERT INTO Notes (Id, Title, FilePath, CreatedAt, UpdatedAt, MarkdownText, Prompt)
                  VALUES ($id, $title, $path, $createdAt, $updatedAt, $markdown, $prompt);
                  """
                : """
                  UPDATE Notes
                  SET Title = $title,
                      UpdatedAt = $updatedAt,
                      MarkdownText = $markdown,
                      Prompt = $prompt
                  WHERE Id = $id;
                  """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$title", note.Title);
            command.Parameters.AddWithValue("$path", Path.GetFullPath(note.FilePath));
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            command.Parameters.AddWithValue("$markdown", markdown);
            command.Parameters.AddWithValue("$prompt", prompt ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteSources = connection.CreateCommand())
        {
            deleteSources.Transaction = transaction;
            deleteSources.CommandText = "DELETE FROM NoteSources WHERE NoteId = $id;";
            deleteSources.Parameters.AddWithValue("$id", id);
            await deleteSources.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < note.SourcePaths.Count; index++)
        {
            await using var insertSource = connection.CreateCommand();
            insertSource.Transaction = transaction;
            insertSource.CommandText = """
                INSERT INTO NoteSources (NoteId, RecordingId, SourcePath, Position)
                VALUES ($noteId, $recordingId, $sourcePath, $position);
                """;
            insertSource.Parameters.AddWithValue("$noteId", id);
            insertSource.Parameters.AddWithValue(
                "$recordingId",
                index < note.RecordingIds.Count ? note.RecordingIds[index].ToString("N") : string.Empty);
            insertSource.Parameters.AddWithValue("$sourcePath", note.SourcePaths[index]);
            insertSource.Parameters.AddWithValue("$position", index);
            await insertSource.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var normalized = query?.Trim() ?? string.Empty;
        command.CommandText = """
            SELECT n.Id,
                   n.Title,
                   n.FilePath,
                   n.CreatedAt,
                   n.UpdatedAt,
                   n.MarkdownText,
                   (SELECT COUNT(*) FROM NoteSources s WHERE s.NoteId = n.Id) AS SourceCount
            FROM Notes n
            ORDER BY n.UpdatedAt DESC;
            """;

        var result = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var title = reader.GetString(1);
            var markdown = reader.GetString(5);
            if (!string.IsNullOrEmpty(normalized)
                && !title.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var created = DateTimeOffset.TryParse(reader.GetString(3), out var parsedCreated)
                ? parsedCreated
                : DateTimeOffset.MinValue;
            var updated = DateTimeOffset.TryParse(reader.GetString(4), out var parsedUpdated)
                ? parsedUpdated
                : created;

            result.Add(new HistoryEntry(
                reader.GetString(0),
                title,
                reader.GetString(2),
                created,
                updated,
                checked((int)reader.GetInt64(6)),
                BuildPreview(markdown)));
        }

        return result;
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Notes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            var directory = Path.GetDirectoryName(_databasePath)
                ?? throw new IOException($"Не удалось определить папку базы истории: {_databasePath}");
            Directory.CreateDirectory(directory);

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS Notes (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    FilePath TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    MarkdownText TEXT NOT NULL,
                    Prompt TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS NoteSources (
                    NoteId TEXT NOT NULL,
                    RecordingId TEXT NOT NULL,
                    SourcePath TEXT NOT NULL,
                    Position INTEGER NOT NULL,
                    PRIMARY KEY (NoteId, Position),
                    FOREIGN KEY (NoteId) REFERENCES Notes(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_Notes_UpdatedAt ON Notes(UpdatedAt DESC);
                CREATE INDEX IF NOT EXISTS IX_NoteSources_NoteId ON NoteSources(NoteId);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<string?> FindIdByPathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string path,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Notes WHERE FilePath = $path COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        return (await command.ExecuteScalarAsync(cancellationToken)) as string;
    }

    private static string BuildPreview(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "Нет текстового содержимого.";

        var lines = markdown
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToArray();

        var text = string.Join(" ", lines);
        if (string.IsNullOrWhiteSpace(text))
            return "Нет текстового содержимого.";

        return text.Length <= 260 ? text : text[..257] + "…";
    }
}
