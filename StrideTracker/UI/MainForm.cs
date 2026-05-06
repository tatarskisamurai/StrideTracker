using System.Diagnostics;
using System.Drawing;
using StrideTracker.Configuration;
using StrideTracker.Tracking;

namespace StrideTracker.UI;

public sealed class MainForm : Form
{
    private readonly AppUsageTracker _tracker = new();
    private readonly System.Windows.Forms.Timer _samplingTimer = new();
    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly string _sessionStatePath = BuildSessionStatePath();
    private readonly string _settingsPath = BuildSettingsPath();
    private readonly AppSettingsStore _settingsStore = new();

    private readonly MenuStrip _menuStrip = new();
    private readonly ToolStripMenuItem _settingsMenuItem = new("Settings");
    private readonly ToolStripMenuItem _preferencesMenuItem = new("Preferences...");
    private readonly UsageListView _usageListView = new();
    private readonly ImageList _appIcons = new();
    private readonly Dictionary<string, string> _iconKeyByApp = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultIconKey = "default";
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();

    private AppSettings _settings;
    private bool _isTracking;
    private int _secondsSinceLastAutosave;

    public MainForm()
    {
        _settings = _settingsStore.Load(_settingsPath);

        Text = "Stride Tracker";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
        else
        {
            var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon is not null)
            {
                Icon = appIcon;
            }
        }

        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 420);

        InitializeLayout();
        BindEvents();
        ApplySettings(_settings);
        RestorePreviousState();
        RefreshUsageList();
        UpdateUiState();

        if (_settings.StartTrackingOnLaunch)
        {
            StartTracking();
        }
    }

    private void InitializeLayout()
    {
        _menuStrip.Items.Add(_settingsMenuItem);
        _settingsMenuItem.DropDownItems.Add(_preferencesMenuItem);
        MainMenuStrip = _menuStrip;

        _appIcons.ColorDepth = ColorDepth.Depth32Bit;
        _appIcons.ImageSize = new Size(16, 16);
        _appIcons.Images.Add(_defaultIconKey, SystemIcons.Application.ToBitmap());

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

        controlsPanel.Controls.AddRange([_startButton, _stopButton]);
        Controls.Add(_menuStrip);
        Controls.Add(_usageListView);
        Controls.Add(controlsPanel);
    }

    private void BindEvents()
    {
        _samplingTimer.Tick += OnSamplingTick;
        _startButton.Click += (_, _) => StartTracking();
        _stopButton.Click += (_, _) => StopTracking();
        _preferencesMenuItem.Click += (_, _) => OpenSettingsDialog();
        FormClosing += OnFormClosing;
    }

    private void StartTracking()
    {
        if (_isTracking)
        {
            return;
        }

        _isTracking = true;
        _secondsSinceLastAutosave = 0;
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
            RefreshUsageList();
            return;
        }

        _tracker.AddSample(sample);
        EnsureAppIcon(sample.ProcessName, sample.ProcessId, sample.ExecutablePath);
        _secondsSinceLastAutosave += _settings.SamplingIntervalSeconds;
        if (_secondsSinceLastAutosave >= _settings.AutosaveIntervalSeconds)
        {
            PersistState();
            _secondsSinceLastAutosave = 0;
        }

        RefreshUsageList();
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
            EnsureAppIcon(appName, processId: null, executablePath: _tracker.GetKnownExecutablePath(appName));
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
        _startButton.Enabled = !_isTracking;
        _stopButton.Enabled = _isTracking;
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

    private void EnsureAppIcon(string appName, int? processId, string? executablePath)
    {
        if (_iconKeyByApp.TryGetValue(appName, out var iconKey) && iconKey != _defaultIconKey)
        {
            return;
        }

        _iconKeyByApp[appName] = TryLoadProcessIcon(appName, processId, executablePath) ?? _defaultIconKey;
    }

    private string? TryLoadProcessIcon(string appName, int? processId, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) && processId is int actualProcessId)
        {
            try
            {
                using var process = Process.GetProcessById(actualProcessId);
                executablePath = process.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
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

    private static string BuildSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StrideTracker", "settings.json");
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = new AppSettings
        {
            SamplingIntervalSeconds = Math.Clamp(settings.SamplingIntervalSeconds, 1, 30),
            AutosaveIntervalSeconds = Math.Clamp(settings.AutosaveIntervalSeconds, 5, 300),
            StartTrackingOnLaunch = settings.StartTrackingOnLaunch
        };

        _samplingTimer.Interval = _settings.SamplingIntervalSeconds * 1000;
    }

    private void OpenSettingsDialog()
    {
        using var settingsForm = new SettingsForm(_settings);
        if (settingsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ApplySettings(settingsForm.ResultSettings);
        _settingsStore.Save(_settingsPath, _settings);

        if (_isTracking)
        {
            _samplingTimer.Stop();
            _samplingTimer.Start();
        }
    }
}
