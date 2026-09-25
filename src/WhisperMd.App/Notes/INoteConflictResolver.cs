namespace WhisperMd.App.Notes;

public interface INoteConflictResolver
{
    Task<NoteConflictAction> ResolveAsync(string existingPath, CancellationToken cancellationToken = default);
}
