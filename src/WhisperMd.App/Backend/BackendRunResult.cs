namespace WhisperMd.App.Backend;

public sealed record BackendRunResult(
    int ExitCode,
    WhisperBackendEvent ResultEvent,
    IReadOnlyList<WhisperBackendEvent> Events,
    IReadOnlyList<BackendLogLine> TechnicalLog);
