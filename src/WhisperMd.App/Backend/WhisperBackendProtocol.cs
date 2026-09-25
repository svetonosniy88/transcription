using System.Text.Json;

namespace WhisperMd.App.Backend;

public static class WhisperBackendProtocol
{
    public const int CurrentVersion = 1;
    public const string EventPrefix = "WHISPERMD_EVENT ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsProtocolLine(string? line)
        => line?.StartsWith(EventPrefix, StringComparison.Ordinal) == true;

    public static bool TryParse(
        string line,
        out WhisperBackendEvent? backendEvent,
        out string? error)
    {
        backendEvent = null;
        error = null;

        if (!IsProtocolLine(line))
            return false;

        var json = line[EventPrefix.Length..].Trim();
        if (json.Length == 0)
        {
            error = "Backend protocol line contains no JSON payload.";
            return false;
        }

        try
        {
            backendEvent = JsonSerializer.Deserialize<WhisperBackendEvent>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Invalid backend event JSON: {ex.Message}";
            return false;
        }

        if (backendEvent is null)
        {
            error = "Backend protocol JSON produced a null event.";
            return false;
        }

        if (backendEvent.Version != CurrentVersion)
        {
            error = $"Unsupported backend protocol version {backendEvent.Version}. Expected {CurrentVersion}.";
            backendEvent = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(backendEvent.Type))
        {
            error = "Backend event does not contain a type.";
            backendEvent = null;
            return false;
        }

        if (!ValidateRequiredFields(backendEvent, out error))
        {
            backendEvent = null;
            return false;
        }

        return true;
    }

    private static bool ValidateRequiredFields(WhisperBackendEvent backendEvent, out string? error)
    {
        error = backendEvent.Kind switch
        {
            WhisperBackendEventKind.Stage when string.IsNullOrWhiteSpace(backendEvent.Stage)
                => "Stage event does not contain stage.",
            WhisperBackendEventKind.Progress when backendEvent.Value is null
                => "Progress event does not contain value.",
            WhisperBackendEventKind.Progress when backendEvent.Value is < 0 or > 100
                => "Progress event value must be between 0 and 100.",
            WhisperBackendEventKind.Result when string.IsNullOrWhiteSpace(backendEvent.ResultDirectory) ||
                                                string.IsNullOrWhiteSpace(backendEvent.TxtPath) ||
                                                string.IsNullOrWhiteSpace(backendEvent.SrtPath) ||
                                                string.IsNullOrWhiteSpace(backendEvent.JsonPath)
                => "Result event does not contain all required output paths.",
            WhisperBackendEventKind.Warning when string.IsNullOrWhiteSpace(backendEvent.Message)
                => "Warning event does not contain message.",
            WhisperBackendEventKind.Error when string.IsNullOrWhiteSpace(backendEvent.Message)
                => "Error event does not contain message.",
            WhisperBackendEventKind.BackendSelected when string.IsNullOrWhiteSpace(backendEvent.Backend)
                => "backend_selected event does not contain backend.",
            WhisperBackendEventKind.Fallback when string.IsNullOrWhiteSpace(backendEvent.FromBackend) ||
                                                   string.IsNullOrWhiteSpace(backendEvent.ToBackend)
                => "Fallback event does not contain fromBackend/toBackend.",
            _ => null
        };

        return error is null;
    }
}
