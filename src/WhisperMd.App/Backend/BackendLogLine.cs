namespace WhisperMd.App.Backend;

public enum BackendLogStream
{
    StandardOutput,
    StandardError,
    Protocol
}

public sealed record BackendLogLine(
    DateTimeOffset Timestamp,
    BackendLogStream Stream,
    string Text);
