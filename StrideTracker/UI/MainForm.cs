using System.Diagnostics;
using System.Drawing;
using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new() { Interval = 1000 };
    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly string _sessionStatePath = BuildSessionStatePath();

    private readonly Label _statusLabel = new();
    private readonly Label _currentAppLabel = new();
    private readonly UsageListView _usageListView = new();
    private readonly ImageList _appIcons = new();
    private readonly Dictionary<string, string> _iconKeyByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultIconKey = "default";
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _saveButton = new();

    private bool _isTracking;
    private DateTimeOffset? _startedAt;
    private int _ticksSinceLastAutosave;

    public MainForm()
    {
        Text = "Stride Tracker";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 420);

        InitializeLayout();
        BindEvents();
        RestorePreviousState();
        RefreshUsageList();
        UpdateUiState();
    }

    private void InitializeLayout()
    {
        _appIcons.ColorDepth = ColorDepth.Depth32Bit;
        _appIcons.ImageSize = new Size(16, 16);
        _appIcons.Images.Add(_defaultIconKey, SystemIcons.Application.ToBitmap());

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.Padding = new Padding(12, 12, 12, 6);
        _statusLabel.Font = new Font(_statusLabel.Font, FontStyle.Bold);
        _statusLabel.Text = "Status: Stopped";

        _currentAppLabel.Dock = DockStyle.Top;
        _currentAppLabel.Padding = new Padding(12, 0, 12, 10);
        _currentAppLabel.Text = "Current app: -";

        _usageListView.Dock = DockStyle.Fill;
        _usageListView.View = View.Details;
        _usageListView.FullRowSelect = true;
        _usageListView.GridLines = true;
        _usageListView.SmallImageList = _appIcons;
        _usageListView.Columns.Add("Application", 420);
        _usageListView.Columns.Add("Time", 160);
        _usageListView.Columns.Add("Percent", 120);

        var controlsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 10, 12, 10),
            WrapContents = false
        };

        _startButton.Text = "Start";
        _startButton.Width = 100;

        _stopButton.Text = "Stop";
        _stopButton.Width = 100;

        _saveButton.Text = "Save report";
        _saveButton.Width = 120;

        controlsPanel.Controls.AddRange([_startButton, _stopButton, _saveButton]);
        Controls.Add(_usageListView);
        Controls.Add(controlsPanel);
        Controls.Add(_currentAppLabel);
        Controls.Add(_statusLabel);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _startButton.Click += (_, _) => StartTracking();
        _stopButton.Click += (_, _) => StopTracking();
        _saveButton.Click += async (_, _) => await SaveReportWithDialogAsync();
        FormClosing += OnFormClosing;
    }

    private void StartTracking()
    {
        if (_isTracking)
        {
            return;
        }

        _isTracking = true;
        _startedAt = DateTimeOffset.Now;
        _ticksSinceLastAutosave = 0;
        _samplingTimer.Start();
        UpdateUiState();
    }

    private void StopTracking()
    {
        if (!_isTracking)
        {
            return;
        }

        _samplingTimer.Stop();
        _tracker.Flush(DateTimeOffset.UtcNow);
        PersistState();
        _isTracking = false;
        _currentAppLabel.Text = "Current app: -";
        UpdateUiState();
        RefreshUsageList();
    }

    private void OnSamplingTick(object? sender, EventArgs e)
    {
        var sample = AppUsageTracker.TryGetActiveWindowInfo();
        if (sample is null)
        {
            return;
        }

        if (sample.ProcessId == _selfProcessId)
        {
            // Stop attribution when our own UI is focused.
            _tracker.Flush(sample.CapturedAtUtc);
            _currentAppLabel.Text = "Current app: (Stride Tracker UI is focused)";
            RefreshUsageList();
            return;
        }

        _tracker.AddSample(sample);
        EnsureAppIcon(sample.ProcessName, sample.ProcessId);
        _ticksSinceLastAutosave++;
        if (_ticksSinceLastAutosave >= 10)
        {
            PersistState();
            _ticksSinceLastAutosave = 0;
        }

        var title = string.IsNullOrWhiteSpace(sample.WindowTitle) ? "(no title)" : sample.WindowTitle;
        _currentAppLabel.Text = $"Current app: {sample.ProcessName} | {title}";
        RefreshUsageList();
    }

    private async Task SaveReportWithDialogAsync()
    {
        using var saveDialog = new SaveFileDialog
        {
            Title = "Save usage report",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"usage-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_isTracking)
        {
            var sample = AppUsageTracker.TryGetActiveWindowInfo();
            if (sample is not null && sample.ProcessId != _selfProcessId)
            {
                _tracker.AddSample(sample);
            }
            else if (sample is not null)
            {
                _tracker.Flush(sample.CapturedAtUtc);
            }
        }

        try
        {
            await _tracker.SaveJsonAsync(saveDialog.FileName);
            MessageBox.Show(this, $"Report saved to:\n{saveDialog.FileName}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot save report:\n{ex.Message}", "Stride Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshUsageList()
    {
        _usageListView.BeginUpdate();

        var snapshot = _tracker.DurationsByApp
            .OrderByDescending(x => x.Value)
            .ToArray();

        var total = snapshot.Sum(x => x.Value.TotalSeconds);
        var existingByApp = _usageListView.Items
            .Cast<ListViewItem>()
            .Where(item => item.Tag is string)
            .ToDictionary(item => (string)item.Tag!, item => item, StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetIndex = 0;

        foreach (var (appName, duration) in snapshot)
        {
            seenApps.Add(appName);
            var percent = total > 0 ? duration.TotalSeconds / total * 100d : 0d;
            if (!existingByApp.TryGetValue(appName, out var item))
            {
                item = new ListViewItem(appName)
                {
                    Tag = appName
                };
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(string.Empty);
                _usageListView.Items.Add(item);
            }

            item.Text = appName;
            item.ImageKey = GetIconKeyForApp(appName);
            item.SubItems[1].Text = duration.ToString(@"hh\:mm\:ss");
            item.SubItems[2].Text = $"{percent:0.0}%";

            if (item.Index != targetIndex)
            {
                _usageListView.Items.RemoveAt(item.Index);
                _usageListView.Items.Insert(targetIndex, item);
            }

            targetIndex++;
        }

        for (var index = _usageListView.Items.Count - 1; index >= 0; index--)
        {
            if (_usageListView.Items[index].Tag is not string appName || seenApps.Contains(appName))
            {
                continue;
            }

            _usageListView.Items.RemoveAt(index);
        }

        _usageListView.EndUpdate();
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        _statusLabel.Text = _isTracking
            ? $"Status: Tracking (started {_startedAt:HH:mm:ss})"
            : "Status: Stopped";

        _startButton.Enabled = !_isTracking;
        _stopButton.Enabled = _isTracking;
        _saveButton.Enabled = _tracker.DurationsByApp.Count > 0 || _isTracking;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isTracking)
        {
            StopTracking();
        }

        PersistState();
    }

    private string GetIconKeyForApp(string appName)
    {
        if (_iconKeyByApp.TryGetValue(appName, out var iconKey))
        {
            return iconKey;
        }

        return _defaultIconKey;
    }

    private void EnsureAppIcon(string appName, int processId)
    {
        if (_iconKeyByApp.TryGetValue(appName, out var iconKey) && iconKey != _defaultIconKey)
        {
            return;
        }

        _iconKeyByApp[appName] = TryLoadProcessIcon(appName, processId) ?? _defaultIconKey;
    }

    private string? TryLoadProcessIcon(string appName, int processId)
    {
        string? executablePath;
        try
        {
            using var process = Process.GetProcessById(processId);
            executablePath = process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        Icon? icon;
        try
        {
            icon = Icon.ExtractAssociatedIcon(executablePath);
        }
        catch
        {
            return null;
        }

        if (icon is null)
        {
            return null;
        }

        using (icon)
        {
            var iconKey = $"app:{appName}";
            if (!_appIcons.Images.ContainsKey(iconKey))
            {
                _appIcons.Images.Add(iconKey, icon.ToBitmap());
            }

            return iconKey;
        }
    }

    private void RestorePreviousState()
    {
        try
        {
            _tracker.LoadState(_sessionStatePath);
        }
        catch
        {
            // Ignore corrupted cache and continue with empty state.
        }
    }

    private void PersistState()
    {
        try
        {
            _tracker.SaveState(_sessionStatePath);
        }
        catch
        {
            // Ignore save errors to avoid blocking app close.
        }
    }

    private static string BuildSessionStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "tracker-state.json");
    }
}
