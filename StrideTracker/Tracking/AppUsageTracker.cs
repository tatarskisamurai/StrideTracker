using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace StrideTracker.Tracking;

public sealed class AppUsageTracker
{
    private readonly Dictionary<string, TimeSpan> _durationsByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _executablePathByApp = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private ActiveWindowInfo? _lastSample;

    public IReadOnlyDictionary<string, TimeSpan> DurationsByApp => _durationsByApp;

    public void AddSample(ActiveWindowInfo sample)
    {
        RememberExecutablePath(sample.ProcessName, sample.ExecutablePath);

        if (_lastSample is not null)
        {
            var elapsed = sample.CapturedAtUtc - _lastSample.CapturedAtUtc;
            if (elapsed > TimeSpan.Zero)
            {
                AddDuration(_lastSample.ProcessName, elapsed);
            }
        }

        _lastSample = sample;
    }

    public void Flush(DateTimeOffset stopTimeUtc)
    {
        if (_lastSample is null)
        {
            return;
        }

        var elapsed = stopTimeUtc - _lastSample.CapturedAtUtc;
        if (elapsed > TimeSpan.Zero)
        {
            AddDuration(_lastSample.ProcessName, elapsed);
        }

        _lastSample = null;
    }

    public async Task SaveJsonAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var data = _durationsByApp
            .OrderByDescending(x => x.Value)
            .Select(x => new AppUsageReportEntry(x.Key, x.Value.TotalSeconds))
            .ToArray();
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
    }

    public void SaveState(string outputPath)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var state = new TrackerState(
            SavedAtUtc: DateTimeOffset.UtcNow,
            Entries: GetOrderedEntries());

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(outputPath, json);
    }

    public void LoadState(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            return;
        }

        var json = File.ReadAllText(inputPath);
        var state = JsonSerializer.Deserialize<TrackerState>(json);
        if (state?.Entries is null)
        {
            return;
        }

        _durationsByApp.Clear();
        _executablePathByApp.Clear();
        _lastSample = null;

        foreach (var entry in state.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.AppName) || entry.Seconds <= 0)
            {
                continue;
            }

            _durationsByApp[entry.AppName] = TimeSpan.FromSeconds(entry.Seconds);
            RememberExecutablePath(entry.AppName, entry.ExecutablePath);
        }
    }

    public string? GetKnownExecutablePath(string appName)
    {
        if (_executablePathByApp.TryGetValue(appName, out var path))
        {
            return path;
        }

        return null;
    }

    public static ActiveWindowInfo? TryGetActiveWindowInfo()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        if (GetWindowThreadProcessId(handle, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        string processName;
        string? executablePath = null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
        }
        catch
        {
            return null;
        }

        var titleBuilder = new StringBuilder(512);
        _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
        var windowTitle = titleBuilder.ToString().Trim();

        return new ActiveWindowInfo(
            ProcessId: (int)processId,
            ProcessName: processName,
            WindowTitle: windowTitle,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            ExecutablePath: executablePath);
    }

    private void AddDuration(string appName, TimeSpan elapsed)
    {
        if (_durationsByApp.TryGetValue(appName, out var current))
        {
            _durationsByApp[appName] = current + elapsed;
            return;
        }

        _durationsByApp[appName] = elapsed;
    }

    private sealed record AppUsageReportEntry(string AppName, double Seconds);
    private sealed record AppUsageEntry(string AppName, double Seconds, string? ExecutablePath);
    private sealed record TrackerState(DateTimeOffset SavedAtUtc, AppUsageEntry[] Entries);

    private AppUsageEntry[] GetOrderedEntries()
    {
        return _durationsByApp
            .OrderByDescending(x => x.Value)
            .Select(x => new AppUsageEntry(
                AppName: x.Key,
                Seconds: x.Value.TotalSeconds,
                ExecutablePath: GetKnownExecutablePath(x.Key)))
            .ToArray();
    }

    private void RememberExecutablePath(string appName, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        _executablePathByApp[appName] = executablePath;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
