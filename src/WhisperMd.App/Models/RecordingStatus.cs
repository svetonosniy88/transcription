namespace WhisperMd.App.Models;

public enum RecordingStatus
{
    Queued,
    Preparing,
    Transcribing,
    BuildingResult,
    Completed,
    Failed,
    Cancelled
}
