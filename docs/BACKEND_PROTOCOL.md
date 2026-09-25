# WhisperMd backend protocol v1

`transcribe.ps1` remains the single orchestration layer for FFmpeg and `whisper.cpp`.
The WPF application communicates with it through a line-oriented protocol on stdout.

## Transport

A machine-readable event is exactly one UTF-8 line beginning with:

```text
WHISPERMD_EVENT
```

The remainder of that line is one compact JSON object.

Example:

```text
WHISPERMD_EVENT {"version":1,"type":"progress","timestamp":"2026-09-24T07:30:00.0000000+00:00","value":45}
```

Only lines with this prefix are protocol messages.
All other stdout/stderr lines are technical/human-readable log output and must not be parsed as application state.

The old human-readable `Results:` summary is temporarily retained for compatibility with the legacy WinForms GUI. New WPF code must never depend on it.

## Common fields

Every event contains:

- `version` — protocol version, currently `1`;
- `type` — event type;
- `timestamp` — UTC ISO-8601 timestamp.

## Event types

### `stage`

```json
{"version":1,"type":"stage","timestamp":"...","stage":"preparing"}
```

Known stages in v1:

- `preparing` — FFmpeg conversion;
- `transcribing` — `whisper.cpp` execution;
- `validating` — output file verification/cleanup;
- `completed` — successful end of one backend invocation.

### `progress`

```json
{"version":1,"type":"progress","timestamp":"...","value":45}
```

`value` is in the inclusive range `0..100`.
It is derived from the `whisper.cpp --print-progress` / `-pp` output.

If Vulkan fails in `auto` mode and the backend falls back to CPU, progress may restart from `0` for the second attempt.

### `backend_selected`

```json
{"version":1,"type":"backend_selected","timestamp":"...","backend":"vulkan"}
```

Sent immediately before each Whisper attempt. Therefore an automatic fallback can produce two `backend_selected` events.

### `fallback`

```json
{"version":1,"type":"fallback","timestamp":"...","fromBackend":"vulkan","toBackend":"cpu","exitCode":1}
```

Sent when `auto` mode retries a failed Vulkan run on CPU.

### `warning`

```json
{"version":1,"type":"warning","timestamp":"...","message":"..."}
```

A non-fatal condition. The current implementation emits it for Vulkan → CPU fallback.

### `result`

```json
{
  "version": 1,
  "type": "result",
  "timestamp": "...",
  "backend": "vulkan",
  "resultDirectory": "C:\\Base\\Transcription\\output\\...",
  "txtPath": "...txt",
  "srtPath": "...srt",
  "jsonPath": "...json"
}
```

This is the authoritative machine-readable result. WPF must use it instead of parsing `Results:`.

### `error`

```json
{"version":1,"type":"error","timestamp":"...","message":"..."}
```

The script emits `error` before terminating with a non-zero exit code for handled failures.

## Technical log

The WPF backend client keeps protocol events and ordinary log lines separately:

- `WhisperBackendEvent` — application state;
- `BackendLogLine` — diagnostics from stdout/stderr;
- malformed prefixed protocol lines are treated as protocol errors, not silently accepted as state.

## WPF implementation

Relevant files:

```text
src/WhisperMd.App/Backend/
  BackendLogLine.cs
  BackendRunRequest.cs
  BackendRunResult.cs
  ProjectRootLocator.cs
  WhisperBackendClient.cs
  WhisperBackendEvent.cs
  WhisperBackendProtocol.cs
```

`WhisperBackendClient.RunAsync()` executes exactly one `transcribe.ps1` invocation. It intentionally does not own queue orchestration. Starting with stage 4, `TranscriptionJobRunner` consumes this single-recording primitive to execute an immutable `TranscriptionJob` sequentially and continue after per-item failures.

## Context prompt

`transcribe.ps1` now accepts optional:

```text
-Prompt "..."
```

When non-empty it is forwarded to `whisper-cli` as `--prompt`.
