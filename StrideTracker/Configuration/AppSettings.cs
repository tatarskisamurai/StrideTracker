namespace StrideTracker.Configuration;

public sealed class AppSettings
{
    public int SamplingIntervalSeconds { get; set; } = 1;

    public int AutosaveIntervalSeconds { get; set; } = 10;

    public bool StartTrackingOnLaunch { get; set; }

    public string Language { get; set; } = "ru";

    public string Theme { get; set; } = "dark";

    public string TrackingMode { get; set; } = "all";

    public List<string> SelectedTrackedApps { get; set; } = new();
}
