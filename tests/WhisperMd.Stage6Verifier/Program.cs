using WhisperMd.App.Backend;
using WhisperMd.App.Execution;
using WhisperMd.App.Models;
using WhisperMd.App.Notes;

var root = Path.Combine(Path.GetTempPath(), $"WhisperMd-stage6-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    var recordings = new[]
    {
        CreateRecording(root, "a", "TEXT_A"),
        CreateRecording(root, "b", "TEXT_B"),
        CreateRecording(root, "c", "TEXT_C"),
        CreateRecording(root, "d", "TEXT_D")
    };

    var builder = new MarkdownNoteBuilder();

    await VerifySeparateAsync(builder, recordings);
    await VerifyCombinedAsync(builder, recordings);
    await VerifyGroupsAsync(builder, recordings);
    await VerifyFailurePlaceholderAsync(builder, recordings);
    await VerifyWriterConflictsAsync(root);

    Console.WriteLine("PASS: stage6 Markdown verifier");
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
        // Best-effort cleanup only.
    }
}

static async Task VerifySeparateAsync(MarkdownNoteBuilder builder, TestRecording[] recordings)
{
    var run = CreateSuccessfulRun(recordings, NoteMode.Separate, string.Empty, Array.Empty<TranscriptionGroupDefinition>());
    var drafts = await builder.BuildAsync(run);

    Assert(drafts.Count == 4, "Separate must create one draft per successful recording.");
    for (var index = 0; index < drafts.Count; index++)
    {
        Assert(drafts[index].Title == recordings[index].Stem, "Separate title must match source stem.");
        Assert(drafts[index].Markdown.Contains(recordings[index].Text, StringComparison.Ordinal), "Separate Markdown lost transcript text.");
    }
}

static async Task VerifyCombinedAsync(MarkdownNoteBuilder builder, TestRecording[] recordings)
{
    var run = CreateSuccessfulRun(recordings, NoteMode.Combined, "Общая лекция", Array.Empty<TranscriptionGroupDefinition>());
    var drafts = await builder.BuildAsync(run);

    Assert(drafts.Count == 1, "Combined must create exactly one draft.");
    Assert(drafts[0].Title == "Общая лекция", "Combined must preserve explicit note title.");

    var markdown = drafts[0].Markdown;
    var positions = recordings.Select(recording => markdown.IndexOf(recording.Text, StringComparison.Ordinal)).ToArray();
    Assert(positions.All(position => position >= 0), "Combined Markdown lost one or more transcript parts.");
    Assert(positions.SequenceEqual(positions.OrderBy(position => position)), "Combined Markdown changed recording order.");
    Assert(markdown.Contains("## Часть 1", StringComparison.Ordinal) && markdown.Contains("## Часть 4", StringComparison.Ordinal),
        "Combined Markdown must contain structural part headings.");
}

static async Task VerifyGroupsAsync(MarkdownNoteBuilder builder, TestRecording[] recordings)
{
    var group1 = new TranscriptionGroupDefinition(Guid.NewGuid(), "Группа 1");
    var group2 = new TranscriptionGroupDefinition(Guid.NewGuid(), "Группа 2");

    var items = new[]
    {
        new TranscriptionJobItem(recordings[0].Id, recordings[0].InputPath, 0, group1.Id),
        new TranscriptionJobItem(recordings[1].Id, recordings[1].InputPath, 1, group1.Id),
        new TranscriptionJobItem(recordings[2].Id, recordings[2].InputPath, 2, group2.Id),
        new TranscriptionJobItem(recordings[3].Id, recordings[3].InputPath, 3, null)
    };

    var job = new TranscriptionJob(items, NoteMode.CustomGroups, string.Empty, string.Empty, new[] { group1, group2 });
    var run = CreateRun(job, recordings.Select(CreateSuccessfulItemResult).ToArray());
    var drafts = await builder.BuildAsync(run);

    Assert(drafts.Count == 3, "CustomGroups must create two named groups plus one ungrouped note.");
    Assert(drafts.Any(draft => draft.Title == "Группа 1"), "CustomGroups lost first named group.");
    Assert(drafts.Any(draft => draft.Title == "Группа 2"), "CustomGroups lost second named group.");

    var firstGroup = drafts.Single(draft => draft.Title == "Группа 1").Markdown;
    Assert(firstGroup.IndexOf("TEXT_A", StringComparison.Ordinal) < firstGroup.IndexOf("TEXT_B", StringComparison.Ordinal),
        "CustomGroups changed order inside a group.");
    Assert(drafts.Any(draft => draft.Markdown.Contains("TEXT_D", StringComparison.Ordinal)),
        "CustomGroups lost ungrouped recording.");
}

static async Task VerifyFailurePlaceholderAsync(MarkdownNoteBuilder builder, TestRecording[] recordings)
{
    var items = recordings
        .Select((recording, index) => new TranscriptionJobItem(recording.Id, recording.InputPath, index, null))
        .ToArray();
    var job = new TranscriptionJob(items, NoteMode.Combined, string.Empty, "Частичный результат", Array.Empty<TranscriptionGroupDefinition>());

    var results = recordings.Select(CreateSuccessfulItemResult).ToArray();
    results[1] = new TranscriptionItemRunResult(
        recordings[1].Id,
        recordings[1].InputPath,
        1,
        Success: false,
        Cancelled: false,
        BackendResult: null,
        ErrorMessage: "synthetic failure");

    var drafts = await builder.BuildAsync(CreateRun(job, results));
    Assert(drafts.Count == 1, "Partial Combined run must still create an aggregate draft if another part succeeded.");
    Assert(drafts[0].Markdown.Contains("Фрагмент не транскрибирован", StringComparison.Ordinal),
        "Aggregate Markdown must explicitly mark a failed fragment.");
    Assert(drafts[0].Markdown.Contains("synthetic failure", StringComparison.Ordinal),
        "Aggregate Markdown must preserve the failure reason.");
}

static async Task VerifyWriterConflictsAsync(string root)
{
    var outDir = Path.Combine(root, "writer");
    Directory.CreateDirectory(outDir);
    var id = Guid.NewGuid();
    var draft = new MarkdownNoteDraft("conflict", outDir, "# First\n", new[] { id }, new[] { "source.m4a" });

    var initialWriter = new MarkdownNoteWriter(new StaticConflictResolver(NoteConflictAction.SaveCopy));
    var initial = await initialWriter.SaveAsync(new[] { draft });
    Assert(initial.SavedCount == 1, "Initial Markdown write failed.");
    var originalPath = initial.SavedNotes[0].FilePath;

    var copy = await initialWriter.SaveAsync(new[] { draft with { Markdown = "# Copy\n" } });
    Assert(copy.SavedCount == 1 && copy.SavedNotes[0].FilePath.EndsWith("conflict (2).md", StringComparison.OrdinalIgnoreCase),
        "SaveCopy conflict action did not allocate (2).md.");

    var replaceWriter = new MarkdownNoteWriter(new StaticConflictResolver(NoteConflictAction.Replace));
    var replaced = await replaceWriter.SaveAsync(new[] { draft with { Markdown = "# Replaced\n" } });
    Assert(replaced.SavedCount == 1, "Replace conflict action failed.");
    Assert((await File.ReadAllTextAsync(originalPath)).Contains("Replaced", StringComparison.Ordinal),
        "Replace conflict action did not overwrite target content.");

    var cancelWriter = new MarkdownNoteWriter(new StaticConflictResolver(NoteConflictAction.Cancel));
    var skipped = await cancelWriter.SaveAsync(new[] { draft with { Markdown = "# Must not be written\n" } });
    Assert(skipped.SavedCount == 0 && skipped.SkippedCount == 1, "Cancel conflict action must skip the note.");
    Assert(!(await File.ReadAllTextAsync(originalPath)).Contains("Must not be written", StringComparison.Ordinal),
        "Cancel conflict action unexpectedly modified existing Markdown.");
}

static TranscriptionJobRunResult CreateSuccessfulRun(
    TestRecording[] recordings,
    NoteMode mode,
    string combinedTitle,
    IReadOnlyList<TranscriptionGroupDefinition> groups)
{
    var items = recordings
        .Select((recording, index) => new TranscriptionJobItem(recording.Id, recording.InputPath, index, null))
        .ToArray();
    var job = new TranscriptionJob(items, mode, string.Empty, combinedTitle, groups);
    return CreateRun(job, recordings.Select(CreateSuccessfulItemResult).ToArray());
}

static TranscriptionJobRunResult CreateRun(TranscriptionJob job, IReadOnlyList<TranscriptionItemRunResult> results)
    => new(job, DateTimeOffset.Now.AddSeconds(-1), DateTimeOffset.Now, results);

static TranscriptionItemRunResult CreateSuccessfulItemResult(TestRecording recording)
{
    var resultEvent = new WhisperBackendEvent
    {
        Version = 1,
        Type = "result",
        TxtPath = recording.TxtPath,
        ResultDirectory = Path.GetDirectoryName(recording.TxtPath)
    };
    var backend = new BackendRunResult(0, resultEvent, new[] { resultEvent }, Array.Empty<BackendLogLine>());
    return new TranscriptionItemRunResult(recording.Id, recording.InputPath, recording.Order, true, false, backend, null);
}

static TestRecording CreateRecording(string root, string stem, string text)
{
    var inputPath = Path.Combine(root, $"{stem}.m4a");
    var txtPath = Path.Combine(root, $"{stem}.txt");
    File.WriteAllText(inputPath, string.Empty);
    File.WriteAllText(txtPath, text);
    return new TestRecording(Guid.NewGuid(), stem, text, inputPath, txtPath, Order: stem[0] - 'a');
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed record TestRecording(Guid Id, string Stem, string Text, string InputPath, string TxtPath, int Order);

sealed class StaticConflictResolver : INoteConflictResolver
{
    private readonly NoteConflictAction _action;

    public StaticConflictResolver(NoteConflictAction action)
    {
        _action = action;
    }

    public Task<NoteConflictAction> ResolveAsync(string existingPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_action);
    }
}
