namespace WhisperMd.App.Backend;

public enum WhisperBackendEventKind
{
    Stage,
    Progress,
    Result,
    Warning,
    Error,
    BackendSelected,
    Fallback,
    Unknown
}

public sealed record WhisperBackendEvent
{
    public int Version { get; init; }

    public string Type { get; init; } = string.Empty;

    public DateTimeOffset? Timestamp { get; init; }

    public string? Stage { get; init; }

    public double? Value { get; init; }

    public string? Backend { get; init; }

    public string? FromBackend { get; init; }

    public string? ToBackend { get; init; }

    public int? ExitCode { get; init; }

    public string? Message { get; init; }

    public string? ResultDirectory { get; init; }

    public string? TxtPath { get; init; }

    public string? SrtPath { get; init; }

    public string? JsonPath { get; init; }

    public WhisperBackendEventKind Kind => Type.Trim().ToLowerInvariant() switch
    {
        "stage" => WhisperBackendEventKind.Stage,
        "progress" => WhisperBackendEventKind.Progress,
        "result" => WhisperBackendEventKind.Result,
        "warning" => WhisperBackendEventKind.Warning,
        "error" => WhisperBackendEventKind.Error,
        "backend_selected" => WhisperBackendEventKind.BackendSelected,
        "fallback" => WhisperBackendEventKind.Fallback,
        _ => WhisperBackendEventKind.Unknown
    };
}
