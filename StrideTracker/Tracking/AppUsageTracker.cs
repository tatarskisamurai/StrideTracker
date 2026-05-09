using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace StrideTracker.Tracking;

public sealed class AppUsageTracker
{
    private readonly Dictionary<string, TimeSpan> _durationsByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _executablePathByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastLaunchUtcByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<DateOnly, TimeSpan>> _dailyDurationsByApp = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private ActiveWindowInfo? _lastSample;

    public IReadOnlyDictionary<string, TimeSpan> DurationsByApp => _durationsByApp;
    public IReadOnlyCollection<string> KnownAppNames =>
        _durationsByApp.Keys
            .Concat(_executablePathByApp.Keys)
            .Concat(_lastLaunchUtcByApp.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void AddSample(ActiveWindowInfo sample)
    {
        RememberExecutablePath(sample.ProcessName, sample.ExecutablePath);
        RememberLastLaunchUtc(sample.ProcessName, sample.CapturedAtUtc);

        if (_lastSample is not null)
        {
            var elapsed = sample.CapturedAtUtc - _lastSample.CapturedAtUtc;
            if (elapsed > TimeSpan.Zero)
            {
                AddDuration(_lastSample.ProcessName, _lastSample.CapturedAtUtc, sample.CapturedAtUtc);
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
            AddDuration(_lastSample.ProcessName, _lastSample.CapturedAtUtc, stopTimeUtc);
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
        _lastLaunchUtcByApp.Clear();
        _dailyDurationsByApp.Clear();
        _lastSample = null;

        foreach (var entry in state.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.AppName) || entry.Seconds <= 0)
            {
                continue;
            }

            _durationsByApp[entry.AppName] = TimeSpan.FromSeconds(entry.Seconds);
            RememberExecutablePath(entry.AppName, entry.ExecutablePath);
            RememberLastLaunchUtc(entry.AppName, entry.LastLaunchUtc);
            RememberDailyDurations(entry.AppName, entry.DailyDurations);
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

    public DateTimeOffset? GetLastLaunchUtc(string appName)
    {
        if (_lastLaunchUtcByApp.TryGetValue(appName, out var value))
        {
            return value;
        }

        return null;
    }

    public bool TryGetAppDetails(string appName, out AppDetails details)
    {
        if (!_durationsByApp.TryGetValue(appName, out var duration))
        {
            details = default!;
            return false;
        }

        var todayDate = DateOnly.FromDateTime(DateTime.Now);
        var weekStartDate = GetWeekStartDate(todayDate);
        var hasDailyHistory = _dailyDurationsByApp.TryGetValue(appName, out var perDay) && perDay.Count > 0;
        var todayDuration = hasDailyHistory ? GetDurationForDay(appName, todayDate) : duration;
        var weekDuration = hasDailyHistory ? GetDurationForPeriod(appName, weekStartDate, todayDate) : duration;
        var recentDailyDurations = GetRecentDailyDurations(appName, days: 7);

        details = new AppDetails(
            AppName: appName,
            TotalDuration: duration,
            TodayDuration: todayDuration,
            WeekDuration: weekDuration,
            ExecutablePath: GetKnownExecutablePath(appName),
            LastLaunchUtc: GetLastLaunchUtc(appName),
            RecentDailyDurations: recentDailyDurations);
        return true;
    }

    public void MarkLaunched(string appName, DateTimeOffset launchedAtUtc, string? executablePath = null)
    {
        RememberLastLaunchUtc(appName, launchedAtUtc);
        RememberExecutablePath(appName, executablePath);
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

    private void AddDuration(string appName, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var elapsed = endUtc - startUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        if (_durationsByApp.TryGetValue(appName, out var current))
        {
            _durationsByApp[appName] = current + elapsed;
        }
        else
        {
            _durationsByApp[appName] = elapsed;
        }

        AddDailyDurations(appName, startUtc.LocalDateTime, endUtc.LocalDateTime);
    }

    private sealed record AppUsageReportEntry(string AppName, double Seconds);
    private sealed record AppUsageEntry(string AppName, double Seconds, string? ExecutablePath, DateTimeOffset? LastLaunchUtc, DailyDurationEntry[] DailyDurations);
    private sealed record DailyDurationEntry(string Date, double Seconds);
    private sealed record TrackerState(DateTimeOffset SavedAtUtc, AppUsageEntry[] Entries);

    private AppUsageEntry[] GetOrderedEntries()
    {
        return _durationsByApp
            .OrderByDescending(x => x.Value)
            .Select(x => new AppUsageEntry(
                AppName: x.Key,
                Seconds: x.Value.TotalSeconds,
                ExecutablePath: GetKnownExecutablePath(x.Key),
                LastLaunchUtc: GetLastLaunchUtc(x.Key),
                DailyDurations: GetDailyEntries(x.Key)))
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

    private void RememberLastLaunchUtc(string appName, DateTimeOffset? launchedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(appName) || launchedAtUtc is null)
        {
            return;
        }

        _lastLaunchUtcByApp[appName] = launchedAtUtc.Value;
    }

    private void AddDailyDurations(string appName, DateTime localStart, DateTime localEnd)
    {
        if (localEnd <= localStart)
        {
            return;
        }

        if (!_dailyDurationsByApp.TryGetValue(appName, out var perDay))
        {
            perDay = new Dictionary<DateOnly, TimeSpan>();
            _dailyDurationsByApp[appName] = perDay;
        }

        var cursor = localStart;
        while (cursor < localEnd)
        {
            var nextDay = cursor.Date.AddDays(1);
            var segmentEnd = localEnd < nextDay ? localEnd : nextDay;
            var date = DateOnly.FromDateTime(cursor.Date);
            var segmentDuration = segmentEnd - cursor;

            if (perDay.TryGetValue(date, out var existing))
            {
                perDay[date] = existing + segmentDuration;
            }
            else
            {
                perDay[date] = segmentDuration;
            }

            cursor = segmentEnd;
        }
    }

    private static DateOnly GetWeekStartDate(DateOnly today)
    {
        var mondayBasedDayOfWeek = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-mondayBasedDayOfWeek);
    }

    private TimeSpan GetDurationForDay(string appName, DateOnly date)
    {
        if (_dailyDurationsByApp.TryGetValue(appName, out var perDay) && perDay.TryGetValue(date, out var duration))
        {
            return duration;
        }

        return TimeSpan.Zero;
    }

    private TimeSpan GetDurationForPeriod(string appName, DateOnly startDate, DateOnly endDate)
    {
        if (!_dailyDurationsByApp.TryGetValue(appName, out var perDay))
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        foreach (var (date, duration) in perDay)
        {
            if (date < startDate || date > endDate)
            {
                continue;
            }

            total += duration;
        }

        return total;
    }

    private DailyUsageEntry[] GetRecentDailyDurations(string appName, int days)
    {
        if (!_dailyDurationsByApp.TryGetValue(appName, out var perDay))
        {
            return Array.Empty<DailyUsageEntry>();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var result = new List<DailyUsageEntry>(days);
        for (var i = 0; i < days; i++)
        {
            var day = today.AddDays(-i);
            var duration = perDay.TryGetValue(day, out var value) ? value : TimeSpan.Zero;
            result.Add(new DailyUsageEntry(day, duration));
        }

        return result.ToArray();
    }

    private DailyDurationEntry[] GetDailyEntries(string appName)
    {
        if (!_dailyDurationsByApp.TryGetValue(appName, out var perDay))
        {
            return Array.Empty<DailyDurationEntry>();
        }

        return perDay
            .OrderByDescending(x => x.Key)
            .Select(x => new DailyDurationEntry(x.Key.ToString("yyyy-MM-dd"), x.Value.TotalSeconds))
            .ToArray();
    }

    private void RememberDailyDurations(string appName, DailyDurationEntry[]? entries)
    {
        if (string.IsNullOrWhiteSpace(appName) || entries is null || entries.Length == 0)
        {
            return;
        }

        var perDay = new Dictionary<DateOnly, TimeSpan>();
        foreach (var entry in entries)
        {
            if (entry.Seconds <= 0 || string.IsNullOrWhiteSpace(entry.Date))
            {
                continue;
            }

            if (!DateOnly.TryParse(entry.Date, out var date))
            {
                continue;
            }

            perDay[date] = TimeSpan.FromSeconds(entry.Seconds);
        }

        if (perDay.Count > 0)
        {
            _dailyDurationsByApp[appName] = perDay;
        }
    }

    public sealed record AppDetails(
        string AppName,
        TimeSpan TotalDuration,
        TimeSpan TodayDuration,
        TimeSpan WeekDuration,
        string? ExecutablePath,
        DateTimeOffset? LastLaunchUtc,
        DailyUsageEntry[] RecentDailyDurations);

    public sealed record DailyUsageEntry(DateOnly Date, TimeSpan Duration);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
