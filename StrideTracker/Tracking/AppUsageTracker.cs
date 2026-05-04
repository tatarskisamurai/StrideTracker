using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace StrideTracker.Tracking;

public sealed class AppUsageTracker
{
    private readonly Dictionary<string, TimeSpan> _durationsByApp = new(StringComparer.OrdinalIgnoreCase);

    private ActiveWindowInfo? _lastSample;

    public IReadOnlyDictionary<string, TimeSpan> DurationsByApp => _durationsByApp;

    public void AddSample(ActiveWindowInfo sample)
    {
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
            .Select(x => new AppUsageEntry(x.Key, x.Value.TotalSeconds))
            .ToArray();

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, data, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken);
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
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
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
            CapturedAtUtc: DateTimeOffset.UtcNow);
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

    private sealed record AppUsageEntry(string AppName, double Seconds);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
