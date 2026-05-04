namespace StrideTracker.Tracking;

public sealed record ActiveWindowInfo(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    DateTimeOffset CapturedAtUtc,
    string? ExecutablePath);
