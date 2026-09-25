namespace WhisperMd.App.Backend;

public sealed record BackendRunRequest(
    string InputPath,
    string Backend = "auto",
    string Language = "ru",
    int Threads = 8,
    string Prompt = "",
    bool KeepWav = false,
    string? WorkingDirectory = null,
    string? OutputDirectory = null);
