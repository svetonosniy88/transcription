namespace WhisperMd.App.Backend;

/// <summary>
/// Single-recording transcription primitive consumed by higher application layers.
/// UI and queue orchestration depend on this contract rather than on PowerShell directly.
/// </summary>
public interface IWhisperBackendClient
{
    Task<BackendRunResult> RunAsync(
        BackendRunRequest request,
        Action<WhisperBackendEvent>? onEvent = null,
        Action<BackendLogLine>? onTechnicalLog = null,
        CancellationToken cancellationToken = default);
}
