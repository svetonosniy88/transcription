using WhisperMd.App.Backend;
using WhisperMd.App.Execution;
using WhisperMd.App.History;
using WhisperMd.App.Models;
using WhisperMd.App.Notes;
using WhisperMd.App.Settings;

var root = Path.Combine(Path.GetTempPath(), $"WhisperMd-stage789-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    await VerifySettingsAsync(root);
    await VerifyRunnerUsesSettingsAsync(root);
    await VerifyLibraryRoutingAsync(root);
    await VerifyHistoryAsync(root);

    Console.WriteLine("PASS: stage7-9 persistence/history/settings verifier");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    try
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
}

static async Task VerifySettingsAsync(string root)
{
    var path = Path.Combine(root, "settings", "settings.json");
    var library = Path.Combine(root, "library");
    var service = new AppSettingsService(path);

    await service.SaveAsync(new AppSettings("en", "cpu", 5, library, "domain prompt"));
    Assert(File.Exists(path), "Settings file was not created.");
    Assert(Directory.Exists(library), "Saving settings must create the library directory.");

    var reloaded = new AppSettingsService(path).Current;
    Assert(reloaded.Language == "en", "Language did not persist.");
    Assert(reloaded.Backend == "cpu", "Backend did not persist.");
    Assert(reloaded.Threads == 5, "Thread count did not persist.");
    Assert(Path.GetFullPath(reloaded.LibraryDirectory) == Path.GetFullPath(library), "Library directory did not persist.");
    Assert(reloaded.DefaultPrompt == "domain prompt", "Default prompt did not persist.");
}

static async Task VerifyRunnerUsesSettingsAsync(string root)
{
    var input = Path.Combine(root, "runner-input.m4a");
    File.WriteAllText(input, string.Empty);
    var id = Guid.NewGuid();
    var backend = new CapturingBackendClient(root);
    var runner = new TranscriptionJobRunner(backend);
    var job = new TranscriptionJob(
        new[] { new TranscriptionJobItem(id, input, 0, null) },
        NoteMode.Separate,
        "prompt-x",
        string.Empty,
        Array.Empty<TranscriptionGroupDefinition>(),
        Backend: "cpu",
        Language: "en",
        Threads: 3,
        LibraryDirectory: Path.Combine(root, "library"));

    var result = await runner.RunAsync(job);
    Assert(result.CompletedCount == 1, "Synthetic runner did not complete.");
    Assert(backend.LastRequest is not null, "Backend request was not captured.");
    Assert(backend.LastRequest!.Backend == "cpu", "Runner ignored job backend.");
    Assert(backend.LastRequest.Language == "en", "Runner ignored job language.");
    Assert(backend.LastRequest.Threads == 3, "Runner ignored job thread count.");
    Assert(backend.LastRequest.Prompt == "prompt-x", "Runner ignored job prompt.");
}

static async Task VerifyLibraryRoutingAsync(string root)
{
    var sourceDir = Path.Combine(root, "source");
    var library = Path.Combine(root, "library-route");
    Directory.CreateDirectory(sourceDir);
    Directory.CreateDirectory(library);

    var input = Path.Combine(sourceDir, "lecture.m4a");
    var txt = Path.Combine(sourceDir, "lecture.txt");
    File.WriteAllText(input, string.Empty);
    File.WriteAllText(txt, "library routing text");

    var id = Guid.NewGuid();
    var resultEvent = new WhisperBackendEvent { Version = 1, Type = "result", TxtPath = txt, ResultDirectory = sourceDir };
    var backendResult = new BackendRunResult(0, resultEvent, new[] { resultEvent }, Array.Empty<BackendLogLine>());
    var itemResult = new TranscriptionItemRunResult(id, input, 0, true, false, backendResult, null);
    var job = new TranscriptionJob(
        new[] { new TranscriptionJobItem(id, input, 0, null) },
        NoteMode.Separate,
        string.Empty,
        string.Empty,
        Array.Empty<TranscriptionGroupDefinition>(),
        LibraryDirectory: library);
    var run = new TranscriptionJobRunResult(job, DateTimeOffset.Now.AddSeconds(-1), DateTimeOffset.Now, new[] { itemResult });

    var drafts = await new MarkdownNoteBuilder().BuildAsync(run);
    Assert(drafts.Count == 1, "Library routing test did not produce a draft.");
    Assert(Path.GetFullPath(drafts[0].DirectoryPath) == Path.GetFullPath(library), "Markdown draft ignored configured library directory.");
}

static async Task VerifyHistoryAsync(string root)
{
    var db = Path.Combine(root, "history", "history.db");
    var notesDir = Path.Combine(root, "history-notes");
    Directory.CreateDirectory(notesDir);

    var firstPath = Path.Combine(notesDir, "first.md");
    var secondPath = Path.Combine(notesDir, "second.md");
    await File.WriteAllTextAsync(firstPath, "# Linear algebra\n\nEigenvalue and matrix content");
    await File.WriteAllTextAsync(secondPath, "# Calculus\n\nCauchy criterion and numerical series. Числовые РЯДЫ.");

    var service = new NoteHistoryService(db);
    var first = new SavedMarkdownNote("Linear algebra", firstPath, new[] { Guid.NewGuid() }, new[] { "a.m4a" });
    var second = new SavedMarkdownNote("Calculus", secondPath, new[] { Guid.NewGuid(), Guid.NewGuid() }, new[] { "b.m4a", "c.m4a" });
    await service.IndexAsync(first, "matrix prompt");
    await service.IndexAsync(second, "series prompt");

    var all = await service.SearchAsync(string.Empty);
    Assert(all.Count == 2, "History did not persist both notes.");
    Assert(all.Any(entry => entry.Title == "Calculus" && entry.SourceCount == 2), "History lost source metadata.");

    var titleSearch = await service.SearchAsync("Calculus");
    Assert(titleSearch.Count == 1 && titleSearch[0].Title == "Calculus", "History title search failed.");

    var contentSearch = await service.SearchAsync("Eigenvalue");
    Assert(contentSearch.Count == 1 && contentSearch[0].Title == "Linear algebra", "History content search failed.");

    var cyrillicSearch = await service.SearchAsync("ряды");
    Assert(cyrillicSearch.Count == 1 && cyrillicSearch[0].Title == "Calculus", "History Cyrillic case-insensitive search failed.");

    var reopened = new NoteHistoryService(db);
    var persisted = await reopened.SearchAsync("Cauchy");
    Assert(persisted.Count == 1 && persisted[0].Title == "Calculus", "History did not survive service restart.");

    await reopened.RemoveAsync(persisted[0].Id);
    var afterRemove = await reopened.SearchAsync(string.Empty);
    Assert(afterRemove.Count == 1 && afterRemove[0].Title == "Linear algebra", "Removing history entry failed.");
    Assert(File.Exists(secondPath), "Removing from history must not delete Markdown file.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class CapturingBackendClient : IWhisperBackendClient
{
    private readonly string _root;

    public CapturingBackendClient(string root)
    {
        _root = root;
    }

    public BackendRunRequest? LastRequest { get; private set; }

    public Task<BackendRunResult> RunAsync(
        BackendRunRequest request,
        Action<WhisperBackendEvent>? onEvent = null,
        Action<BackendLogLine>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        var txt = Path.Combine(_root, "synthetic.txt");
        File.WriteAllText(txt, "synthetic");
        var evt = new WhisperBackendEvent
        {
            Version = 1,
            Type = "result",
            TxtPath = txt,
            ResultDirectory = _root
        };
        return Task.FromResult(new BackendRunResult(0, evt, new[] { evt }, Array.Empty<BackendLogLine>()));
    }
}
